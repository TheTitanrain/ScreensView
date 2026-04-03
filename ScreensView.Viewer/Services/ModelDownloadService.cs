using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace ScreensView.Viewer.Services;

public interface IModelDownloadService
{
    bool IsModelReady { get; }
    string ModelPath { get; }
    string ProjectorPath { get; }
    event EventHandler ModelReady;
    Task DownloadAsync(IProgress<double> progress, CancellationToken ct);
}

public class ModelDownloadService : IModelDownloadService
{
    private const string ModelFileName = "Qwen3.5-2B-Q4_K_M.gguf";
    private const string ProjectorFileName = "mmproj-F16.gguf";
    private const string ModelDownloadUrl =
        "https://huggingface.co/unsloth/Qwen3.5-2B-GGUF/resolve/main/Qwen3.5-2B-Q4_K_M.gguf";
    private const string ProjectorDownloadUrl =
        "https://huggingface.co/unsloth/Qwen3.5-2B-GGUF/resolve/main/mmproj-F16.gguf";

    private readonly HttpClient _http;
    private readonly string _basePath;

    public event EventHandler? ModelReady;

    // Production constructor
    public ModelDownloadService()
    {
        _http = new HttpClient();
        _basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreensView", "models");
        Directory.CreateDirectory(_basePath);
    }

    // Testable constructor — injected HttpMessageHandler and path
    internal ModelDownloadService(HttpMessageHandler handler, string basePath)
        : this(new HttpClient(handler), basePath)
    {
    }

    private ModelDownloadService(HttpClient http, string basePath)
    {
        _http = http;
        _basePath = basePath;
        Directory.CreateDirectory(_basePath);
    }

    public string ModelPath => Path.Combine(_basePath, ModelFileName);
    public string ProjectorPath => Path.Combine(_basePath, ProjectorFileName);

    private string PartPath => ModelPath + ".part";
    private string ProjectorPartPath => ProjectorPath + ".part";

    public bool IsModelReady =>
        File.Exists(ModelPath)
        && File.Exists(ProjectorPath)
        && !File.Exists(PartPath)
        && !File.Exists(ProjectorPartPath);

    public async Task DownloadAsync(IProgress<double> progress, CancellationToken ct)
    {
        await DownloadArtifactAsync(
            ModelDownloadUrl,
            ModelPath,
            progress,
            progressOffset: 0,
            progressSpan: 50,
            ct);
        await DownloadArtifactAsync(
            ProjectorDownloadUrl,
            ProjectorPath,
            progress,
            progressOffset: 50,
            progressSpan: 50,
            ct);

        progress.Report(100.0);
        ModelReady?.Invoke(this, EventArgs.Empty);
    }

    private async Task DownloadArtifactAsync(
        string url,
        string finalPath,
        IProgress<double> progress,
        double progressOffset,
        double progressSpan,
        CancellationToken ct)
    {
        var partPath = finalPath + ".part";
        if (File.Exists(finalPath) && !File.Exists(partPath))
        {
            progress.Report(progressOffset + progressSpan);
            return;
        }

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        long existingBytes = 0;
        if (File.Exists(partPath))
        {
            existingBytes = new FileInfo(partPath).Length;
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var append = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append && File.Exists(partPath))
        {
            File.Delete(partPath);
            existingBytes = 0;
        }

        var totalBytes = (response.Content.Headers.ContentLength ?? 0) + existingBytes;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var file = new FileStream(
            partPath,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        var buffer = new byte[81920];
        long downloaded = existingBytes;
        int read;

        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;
            if (totalBytes > 0)
                progress.Report(progressOffset + (downloaded * progressSpan / totalBytes));
        }

        await file.FlushAsync(ct);
        file.Close();

        if (File.Exists(finalPath))
            File.Delete(finalPath);
        File.Move(partPath, finalPath);
    }
}
