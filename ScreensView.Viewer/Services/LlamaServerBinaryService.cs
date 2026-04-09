using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ScreensView.Viewer.Services;

public interface ILlamaServerBinaryService
{
    bool IsCpuReady { get; }
    bool IsGpuReady(string variant);
    string GetExePath(string variant); // "cpu" | "vulkan" | "cuda"
    string? GetInstalledVersion(string variant);
    Task DownloadAsync(string variant, IProgress<double> progress, CancellationToken ct);
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

    public bool IsCpuReady => IsReady("cpu");

    public bool IsGpuReady(string variant) => IsReady(variant);

    public string GetExePath(string variant) =>
        Path.Combine(VariantDir(variant), "llama-server.exe");

    public string? GetInstalledVersion(string variant)
    {
        var versionFile = Path.Combine(VariantDir(variant), "version.txt");
        return File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : null;
    }

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

    private bool IsReady(string variant)
    {
        var dir = VariantDir(variant);
        if (!File.Exists(Path.Combine(dir, "version.txt"))
            || !File.Exists(Path.Combine(dir, "llama-server.exe")))
        {
            return false;
        }

        return variant != "cuda" || HasCudaRuntimeDlls(dir);
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
}
