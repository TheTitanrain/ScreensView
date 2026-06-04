using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreensView.Viewer.Models;

namespace ScreensView.Viewer.Services;

/// <summary>
/// Loads the user-editable LLM model catalog from <c>%AppData%\ScreensView\models.json</c>.
/// The file is seeded with the built-in models on first run, then merged with the built-ins on
/// every load (user entries override built-ins by Id, new ids are appended). All failure paths
/// fall back to <see cref="ModelDefinition.BuiltIn"/> so startup can never be broken by a bad file.
/// </summary>
public class ModelCatalogService
{
    private readonly string _filePath;
    private readonly IViewerLogService _log;
    private readonly object _fileLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ModelCatalogService(IViewerLogService? log = null)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreensView");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "models.json");
        _log = log ?? new NullViewerLogService();
    }

    internal ModelCatalogService(string filePath, IViewerLogService? log = null)
    {
        _filePath = filePath;
        _log = log ?? new NullViewerLogService();

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    /// <summary>
    /// Returns the active model catalog. Seeds the file with the built-ins when it is missing/empty,
    /// otherwise parses it per-item and merges valid user entries over the built-ins.
    /// Never throws — any failure logs and returns the built-ins.
    /// </summary>
    public IReadOnlyList<ModelDefinition> LoadOrSeed()
    {
        lock (_fileLock)
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    Seed();
                    return ModelDefinition.BuiltIn;
                }

                var json = File.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    Seed();
                    return ModelDefinition.BuiltIn;
                }

                var userEntries = ParseValidEntries(json);
                if (userEntries.Count == 0)
                {
                    // Empty/all-invalid file — keep the user's file untouched so they can fix it.
                    _log.LogWarning("ModelCatalog.LoadOrSeed",
                        "models.json contained no valid entries; using built-in catalog.");
                    return ModelDefinition.BuiltIn;
                }

                return Merge(userEntries);
            }
            catch (Exception ex)
            {
                _log.LogError("ModelCatalog.LoadOrSeed",
                    "Failed to load models.json; using built-in catalog.", ex);
                return ModelDefinition.BuiltIn;
            }
        }
    }

    private void Seed()
    {
        try
        {
            var json = JsonSerializer.Serialize(ModelDefinition.BuiltIn, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _log.LogWarning("ModelCatalog.Seed", $"Could not write seed models.json: {ex.Message}");
        }
    }

    /// <summary>
    /// Per-item parse: one malformed element is skipped, it does not abort the whole load.
    /// A document-level error (not an array, malformed JSON) propagates to the outer catch.
    /// </summary>
    private List<ModelDefinition> ParseValidEntries(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("models.json root must be a JSON array.");

        var result = new List<ModelDefinition>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            ModelDto? dto;
            try
            {
                dto = element.Deserialize<ModelDto>(JsonOptions);
            }
            catch (JsonException)
            {
                continue; // skip this element, keep the rest
            }

            var model = Validate(dto);
            if (model is not null)
                result.Add(model);
        }

        return result;
    }

    /// <summary>Returns a valid <see cref="ModelDefinition"/> or null if the DTO fails validation.</summary>
    private static ModelDefinition? Validate(ModelDto? dto)
    {
        if (dto is null)
            return null;

        if (IsBlank(dto.Id) || IsBlank(dto.DisplayName) || IsBlank(dto.FileName) || IsBlank(dto.DownloadUrl))
            return null;

        if (!IsSafeFileName(dto.FileName))
            return null;

        if (!IsHttpUrl(dto.DownloadUrl))
            return null;

        var hasProjFile = !IsBlank(dto.ProjectorFileName);
        var hasProjUrl = !IsBlank(dto.ProjectorDownloadUrl);
        if (hasProjFile != hasProjUrl) // both-or-neither
            return null;

        if (hasProjFile && (!IsSafeFileName(dto.ProjectorFileName!) || !IsHttpUrl(dto.ProjectorDownloadUrl!)))
            return null;

        return new ModelDefinition(
            dto.Id!.Trim(),
            dto.DisplayName!.Trim(),
            dto.FileName!.Trim(),
            dto.DownloadUrl!.Trim(),
            hasProjFile ? dto.ProjectorFileName!.Trim() : null,
            hasProjFile ? dto.ProjectorDownloadUrl!.Trim() : null);
    }

    /// <summary>
    /// Merge built-ins with valid user entries. User entry with a matching Id (case-insensitive)
    /// overrides the built-in in place; new ids are appended. User entries are de-duplicated by Id,
    /// and any entry whose FileName/ProjectorFileName collides with an already-accepted artifact is
    /// dropped so two models can never share one on-disk file.
    /// </summary>
    private List<ModelDefinition> Merge(List<ModelDefinition> userEntries)
    {
        var result = new List<ModelDefinition>(ModelDefinition.BuiltIn);
        var seenIds = new HashSet<string>(result.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in result)
        {
            seenFiles.Add(m.FileName);
            if (m.ProjectorFileName is not null)
                seenFiles.Add(m.ProjectorFileName);
        }

        var consumedUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in userEntries)
        {
            // De-duplicate user ids (first wins).
            if (!consumedUserIds.Add(entry.Id))
                continue;

            var builtInIndex = result.FindIndex(m => string.Equals(m.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
            if (builtInIndex >= 0)
            {
                // Override a built-in: free its old filenames before claiming the new ones.
                var old = result[builtInIndex];
                if (!CanClaimFiles(entry, seenFiles, old))
                {
                    _log.LogWarning("ModelCatalog.Merge",
                        $"Model '{entry.Id}' dropped: file name collides with another model.");
                    continue;
                }

                seenFiles.Remove(old.FileName);
                if (old.ProjectorFileName is not null)
                    seenFiles.Remove(old.ProjectorFileName);

                ClaimFiles(entry, seenFiles);
                result[builtInIndex] = entry;
            }
            else
            {
                if (!CanClaimFiles(entry, seenFiles, exclude: null))
                {
                    _log.LogWarning("ModelCatalog.Merge",
                        $"Model '{entry.Id}' dropped: file name collides with another model.");
                    continue;
                }

                ClaimFiles(entry, seenFiles);
                seenIds.Add(entry.Id);
                result.Add(entry);
            }
        }

        return result;
    }

    private static bool CanClaimFiles(ModelDefinition entry, HashSet<string> seenFiles, ModelDefinition? exclude)
    {
        bool Taken(string name) =>
            seenFiles.Contains(name)
            && !(exclude is not null
                 && (string.Equals(exclude.FileName, name, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(exclude.ProjectorFileName, name, StringComparison.OrdinalIgnoreCase)));

        if (Taken(entry.FileName))
            return false;
        if (entry.ProjectorFileName is not null && Taken(entry.ProjectorFileName))
            return false;
        // Self-collision: model + projector share a name.
        if (entry.ProjectorFileName is not null
            && string.Equals(entry.FileName, entry.ProjectorFileName, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static void ClaimFiles(ModelDefinition entry, HashSet<string> seenFiles)
    {
        seenFiles.Add(entry.FileName);
        if (entry.ProjectorFileName is not null)
            seenFiles.Add(entry.ProjectorFileName);
    }

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    /// <summary>Basename only: no directory separators, no traversal, not rooted.</summary>
    private static bool IsSafeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var trimmed = value.Trim();
        if (trimmed.Contains("..", StringComparison.Ordinal))
            return false;
        if (trimmed.IndexOfAny(['/', '\\']) >= 0)
            return false;
        if (Path.IsPathRooted(trimmed))
            return false;
        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;
        return string.Equals(Path.GetFileName(trimmed), trimmed, StringComparison.Ordinal);
    }

    private static bool IsHttpUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Nullable mirror of <see cref="ModelDefinition"/> for tolerant per-item deserialization.</summary>
    private sealed class ModelDto
    {
        public string? Id { get; set; }
        public string? DisplayName { get; set; }
        public string? FileName { get; set; }
        public string? DownloadUrl { get; set; }
        public string? ProjectorFileName { get; set; }
        public string? ProjectorDownloadUrl { get; set; }
    }
}
