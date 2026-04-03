using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using ScreensView.Viewer.Models;

namespace ScreensView.Viewer.Services;

public interface IModelDownloadService
{
    bool IsModelReady { get; }
    string ModelPath { get; }
    string ProjectorPath { get; }
    ModelDefinition SelectedModel { get; }
    void SelectModel(ModelDefinition model);
    event EventHandler ModelReady;
    Task DownloadAsync(IProgress<double> progress, CancellationToken ct);
}

public class ModelDownloadService : IModelDownloadService
{
    private ModelDefinition _selectedModel = ModelDefinition.Default;

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

    // Testable constructor — path only (no HTTP needed for file-system tests)
    internal ModelDownloadService(string basePath)
        : this(new HttpClient(), basePath)
    {
    }

    private ModelDownloadService(HttpClient http, string basePath)
    {
        _http = http;
        _basePath = basePath;
        Directory.CreateDirectory(_basePath);
    }

    public ModelDefinition SelectedModel => _selectedModel;

    public void SelectModel(ModelDefinition model)
    {
        _selectedModel = model;
    }

    public string ModelPath => Path.Combine(_basePath, _selectedModel.FileName);
    public string ProjectorPath => Path.Combine(_basePath, _selectedModel.ProjectorFileName ?? "mmproj.gguf");

    public bool IsModelReady =>
        File.Exists(ModelPath) && !File.Exists(ModelPath + ".part")
        && (_selectedModel.ProjectorFileName is null
            || (File.Exists(ProjectorPath) && !File.Exists(ProjectorPath + ".part")));

    public async Task DownloadAsync(IProgress<double> progress, CancellationToken ct)
    {
        var model = _selectedModel; // snapshot — prevents race if SelectModel called during download
        var modelPath = Path.Combine(_basePath, model.FileName);
        var projPath  = Path.Combine(_basePath, model.ProjectorFileName ?? "mmproj.gguf");
        var hasProjector = model.ProjectorDownloadUrl is not null;
        var modelSpan = hasProjector ? 50.0 : 100.0;

        await DownloadArtifactAsync(model.DownloadUrl, modelPath, progress, 0, modelSpan, ct);

        if (hasProjector)
            await DownloadArtifactAsync(model.ProjectorDownloadUrl!, projPath, progress, 50, 50, ct);

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
