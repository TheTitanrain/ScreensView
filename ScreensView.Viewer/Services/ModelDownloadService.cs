using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;

namespace ScreensView.Viewer.Services;

public interface IModelDownloadService
{
    bool IsModelReady { get; }
    string ModelPath { get; }
    event EventHandler ModelReady;
    Task DownloadAsync(IProgress<double> progress, CancellationToken ct);
}

public class ModelDownloadService : IModelDownloadService
{
    private const string ModelFileName = "qwen3.5-2b-q4_k_m.gguf";
    private const string DownloadUrl =
        "https://huggingface.co/unsloth/Qwen3.5-2B-GGUF/resolve/main/qwen3.5-2b-q4_k_m.gguf";

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

    private string PartPath => ModelPath + ".part";

    public bool IsModelReady =>
        File.Exists(ModelPath) && !File.Exists(PartPath);

    public async Task DownloadAsync(IProgress<double> progress, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, DownloadUrl);

        long existingBytes = 0;
        if (File.Exists(PartPath))
        {
            existingBytes = new FileInfo(PartPath).Length;
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = (response.Content.Headers.ContentLength ?? 0) + existingBytes;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var file = new FileStream(PartPath, FileMode.Append, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long downloaded = existingBytes;
        int read;

        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;
            if (totalBytes > 0)
                progress.Report(downloaded * 100.0 / totalBytes);
        }

        await file.FlushAsync(ct);
        file.Close();

        // Atomic rename: .part → final
        if (File.Exists(ModelPath))
            File.Delete(ModelPath);
        File.Move(PartPath, ModelPath);

        progress.Report(100.0);
        ModelReady?.Invoke(this, EventArgs.Empty);
    }
}
