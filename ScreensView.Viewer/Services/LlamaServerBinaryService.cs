using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ScreensView.Viewer.Services;

public interface ILlamaServerBinaryService
{
    BackendCheckResult CheckInstallation(string variant);
    string GetExePath(string variant); // "cpu" | "vulkan" | "cuda"
    Task DownloadAsync(string variant, IProgress<double> progress, CancellationToken ct);
}

public enum BackendInstallState
{
    Missing,
    Incomplete,
    Ready
}

public sealed record BackendCheckResult(
    string Backend,
    BackendInstallState State,
    string? InstalledVersion,
    IReadOnlyList<string> MissingArtifacts,
    string UserMessage)
{
    public bool IsReady => State == BackendInstallState.Ready;
}

public class LlamaServerBinaryService : ILlamaServerBinaryService
{
    private readonly HttpClient _http;
    private readonly string _basePath;

    private const string ReleasesApiUrl = "https://api.github.com/repos/ggerganov/llama.cpp/releases/latest";

    public LlamaServerBinaryService()
        : this(CreateDefaultHttpClient(), DefaultBasePath())
    {
    }

    internal LlamaServerBinaryService(HttpMessageHandler handler, string basePath)
        : this(new HttpClient(handler), basePath)
    {
    }

    private LlamaServerBinaryService(HttpClient http, string basePath)
    {
        _http = http;
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ScreensView", "1.0"));
        _basePath = basePath;
        Directory.CreateDirectory(basePath);
    }

    public BackendCheckResult CheckInstallation(string variant)
    {
        var dir = VariantDir(variant);
        var versionPath = Path.Combine(dir, "version.txt");
        var installedVersion = HasCompleteFile(versionPath)
            ? File.ReadAllText(versionPath).Trim()
            : null;
        var hasAnyFiles = Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any();

        var missingArtifacts = GetRequiredArtifactsForInstallation(variant)
            .Where(artifact => !artifact.Exists(dir))
            .Select(artifact => artifact.Name)
            .ToList();

        if (missingArtifacts.Count == 0)
        {
            return new BackendCheckResult(
                variant,
                BackendInstallState.Ready,
                installedVersion,
                [],
                string.Empty);
        }

        var state = hasAnyFiles ? BackendInstallState.Incomplete : BackendInstallState.Missing;
        var userMessage = state == BackendInstallState.Incomplete
            ? "Бэкенд для распознавания не скачан полностью. Откройте настройки и нажмите \"Скачать бэкенд\"."
            : "Бэкенд для распознавания не скачан. Откройте настройки и нажмите \"Скачать бэкенд\".";

        return new BackendCheckResult(
            variant,
            state,
            installedVersion,
            missingArtifacts,
            userMessage);
    }

    public string GetExePath(string variant) =>
        Path.Combine(VariantDir(variant), "llama-server.exe");

    public async Task DownloadAsync(string variant, IProgress<double> progress, CancellationToken ct)
    {
        var release = await FetchLatestReleaseAsync(ct).ConfigureAwait(false);
        var assets = GetRequiredAssets(variant, release);

        var dir = VariantDir(variant);
        Directory.CreateDirectory(dir);

        for (var index = 0; index < assets.Count; index++)
        {
            var asset = assets[index];
            var zipPath = Path.Combine(dir, asset.Name + ".part");
            var assetProgress = CreateAssetProgress(progress, index, assets.Count);

            await DownloadFileAsync(asset.BrowserDownloadUrl, zipPath, assetProgress, ct).ConfigureAwait(false);
            ExtractBinaries(zipPath, dir);
            File.Delete(zipPath);
        }

        progress.Report(95);
        File.WriteAllText(Path.Combine(dir, "version.txt"), release.TagName);
        progress.Report(100);
    }

    private string VariantDir(string variant) => Path.Combine(_basePath, variant);

    private async Task<GithubRelease> FetchLatestReleaseAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync(ReleasesApiUrl, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<GithubRelease>(json, JsonOptions)
            ?? throw new InvalidOperationException("Не удалось распарсить ответ GitHub API.");
    }

    private async Task DownloadFileAsync(
        string url, string destPath, IProgress<double> progress, CancellationToken ct)
    {
        using var response = await _http.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0;
        await using var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            downloaded += read;
            if (total > 0)
                progress.Report(downloaded * 90.0 / total);
        }
    }

    private static void ExtractBinaries(string zipPath, string destDir)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            var name = Path.GetFileName(entry.FullName);
            if (string.IsNullOrEmpty(name)) continue;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;

            var destPath = Path.Combine(destDir, name);
            entry.ExtractToFile(destPath, overwrite: true);
        }
    }

    private static IReadOnlyList<GithubAsset> GetRequiredAssets(string variant, GithubRelease release)
    {
        var patterns = GetAssetPatterns(variant);
        var assets = new List<GithubAsset>(patterns.Count);

        foreach (var pattern in patterns)
        {
            var asset = release.Assets.FirstOrDefault(a => MatchesPattern(a.Name, pattern))
                ?? throw new InvalidOperationException(
                    $"Не найден файл '{pattern}' в релизе {release.TagName}. " +
                    "Проверьте наличие новой версии llama.cpp.");
            assets.Add(asset);
        }

        return assets;
    }

    private static IReadOnlyList<string> GetAssetPatterns(string variant) => variant switch
    {
        "cpu"    => ["bin-win-cpu-x64.zip"],
        "vulkan" => ["bin-win-vulkan-x64.zip"],
        "cuda"   => ["bin-win-cuda-12", "cudart-llama-bin-win-cuda-12"],
        _        => throw new ArgumentException($"Unknown variant: {variant}", nameof(variant))
    };

    private static bool MatchesPattern(string assetName, string pattern) =>
        assetName.Contains(pattern, StringComparison.OrdinalIgnoreCase)
        && assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    private static IProgress<double> CreateAssetProgress(
        IProgress<double> overallProgress,
        int assetIndex,
        int assetCount)
    {
        var segmentStart = assetIndex * 90.0 / assetCount;
        var segmentSize = 90.0 / assetCount;
        return new Progress<double>(value => overallProgress.Report(segmentStart + (value * segmentSize / 90.0)));
    }

    private static bool HasCudaRuntimeDlls(string dir) =>
        Directory.EnumerateFiles(dir, "cudart64_*.dll", SearchOption.TopDirectoryOnly).Any();

    private static bool HasCompleteFile(string path) =>
        File.Exists(path) && new FileInfo(path).Length > 0;

    private static IReadOnlyList<RequiredArtifact> GetRequiredArtifactsForInstallation(string variant)
    {
        var commonArtifacts = new List<RequiredArtifact>
        {
            RequiredArtifact.ForFile("version.txt"),
            RequiredArtifact.ForFile("llama-server.exe"),
            RequiredArtifact.ForFile("llama.dll"),
            RequiredArtifact.ForFile("ggml.dll"),
            RequiredArtifact.ForFile("ggml-base.dll")
        };

        if (variant == "cuda")
            commonArtifacts.Add(RequiredArtifact.ForPattern("cudart64_*.dll"));

        return commonArtifacts;
    }

    private static string DefaultBasePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreensView", "llama-server");

    private static HttpClient CreateDefaultHttpClient() => new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record GithubRelease(
        [property: System.Text.Json.Serialization.JsonPropertyName("tag_name")] string TagName,
        [property: System.Text.Json.Serialization.JsonPropertyName("assets")] List<GithubAsset> Assets);

    private sealed record GithubAsset(
        [property: System.Text.Json.Serialization.JsonPropertyName("name")] string Name,
        [property: System.Text.Json.Serialization.JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);

    private sealed class RequiredArtifact
    {
        private readonly string _pattern;
        private readonly bool _isPattern;

        private RequiredArtifact(string name, string pattern, bool isPattern)
        {
            Name = name;
            _pattern = pattern;
            _isPattern = isPattern;
        }

        public string Name { get; }

        public bool Exists(string dir)
        {
            if (!Directory.Exists(dir))
                return false;

            if (!_isPattern)
                return HasCompleteFile(Path.Combine(dir, _pattern));

            return Directory.EnumerateFiles(dir, _pattern, SearchOption.TopDirectoryOnly)
                .Any(HasCompleteFile);
        }

        public static RequiredArtifact ForFile(string fileName) => new(fileName, fileName, isPattern: false);

        public static RequiredArtifact ForPattern(string pattern) => new(pattern, pattern, isPattern: true);
    }
}
