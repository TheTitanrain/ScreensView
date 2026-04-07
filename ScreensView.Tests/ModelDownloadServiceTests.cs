using System.Net;
using System.Net.Http;
using System.Text;
using ScreensView.Viewer.Models;
using ScreensView.Viewer.Services;

namespace ScreensView.Tests;

public class ModelDownloadServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public ModelDownloadServiceTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private ModelDownloadService Make(HttpMessageHandler? handler = null)
        => new(handler ?? new NoOpHandler(), _tempDir);

    private string ModelPath => Make().ModelPath;
    private string ProjectorPath => Make().ProjectorPath;

    // ---- IsModelReady ----

    [Fact]
    public void IsModelReady_WhenNoFilesExist_ReturnsFalse()
    {
        Assert.False(Make().IsModelReady);
    }

    [Fact]
    public void IsModelReady_WhenOnlyPartFileExists_ReturnsFalse()
    {
        File.WriteAllText(ModelPath + ".part", "partial");
        Assert.False(Make().IsModelReady);
    }

    [Fact]
    public void IsModelReady_WhenOnlyMainModelExists_ReturnsFalse()
    {
        File.WriteAllText(ModelPath, "model");
        Assert.False(Make().IsModelReady);
    }

    [Fact]
    public void IsModelReady_WhenOnlyProjectorExists_ReturnsFalse()
    {
        File.WriteAllText(ProjectorPath, "projector");
        Assert.False(Make().IsModelReady);
    }

    [Fact]
    public void IsModelReady_WhenMainModelAndProjectorExist_ReturnsTrue()
    {
        File.WriteAllText(ModelPath, "model");
        File.WriteAllText(ProjectorPath, "projector");
        Assert.True(Make().IsModelReady);
    }

    [Fact]
    public void IsModelReady_WhenModelFileIsZeroBytes_ReturnsFalse()
    {
        File.WriteAllBytes(ModelPath, []);
        File.WriteAllText(ProjectorPath, "projector");

        Assert.False(Make().IsModelReady);
    }

    [Fact]
    public void IsModelReady_WhenProjectorFileIsZeroBytes_ReturnsFalse()
    {
        File.WriteAllText(ModelPath, "model");
        File.WriteAllBytes(ProjectorPath, []);

        Assert.False(Make().IsModelReady);
    }

    // ---- Download writes file ----

    [Fact]
    public async Task DownloadAsync_OnSuccess_WritesBothFilesAndFiresModelReady()
    {
        var handler = new RoutingHandler(
            "fake-model-bytes"u8.ToArray(),
            "fake-mmproj-bytes"u8.ToArray());
        var svc = Make(handler);
        bool modelReadyFired = false;
        svc.ModelReady += (_, _) => modelReadyFired = true;

        await svc.DownloadAsync(new Progress<double>(), CancellationToken.None);

        Assert.True(svc.IsModelReady);
        Assert.True(modelReadyFired);
        Assert.True(File.Exists(ModelPath));
        Assert.True(File.Exists(ProjectorPath));
        Assert.False(File.Exists(ModelPath + ".part"));
        Assert.False(File.Exists(ProjectorPath + ".part"));
    }

    // ---- Resume via Range header ----

    [Fact]
    public async Task DownloadAsync_WhenPartFileExists_SendsRangeHeader()
    {
        var existingBytes = "existing"u8.ToArray();
        File.WriteAllBytes(ModelPath + ".part", existingBytes);

        string? rangeHeader = null;
        var handler = new RoutingHandler(
            "more"u8.ToArray(),
            "projector"u8.ToArray(),
            capture: req =>
            {
                if (req.RequestUri?.AbsoluteUri.Contains(ModelDefinition.Default.FileName, StringComparison.OrdinalIgnoreCase) == true)
                    rangeHeader = req.Headers.Range?.ToString();
            });
        var svc = Make(handler);

        await svc.DownloadAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal($"bytes={existingBytes.Length}-", rangeHeader);
    }

    // ---- Cancellation leaves .part ----

    [Fact]
    public async Task DownloadAsync_WhenCancelled_LeavesPartFile()
    {
        var cts = new CancellationTokenSource();
        // Handler cancels after first byte
        var handler = new CancellingHandler(cts, firstChunk: "first"u8.ToArray());
        var svc = Make(handler);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => svc.DownloadAsync(new Progress<double>(), cts.Token));

        Assert.True(File.Exists(ModelPath + ".part"));
        Assert.False(svc.IsModelReady);
    }

    // ---- SelectModel ----

    [Fact]
    public void SelectModel_ChangesIsModelReady()
    {
        var svc = new ModelDownloadService(_tempDir);

        // Write the default model files so IsModelReady is true for the default model
        File.WriteAllText(Path.Combine(_tempDir, ModelDefinition.Default.FileName), "model");
        if (ModelDefinition.Default.ProjectorFileName is not null)
            File.WriteAllText(Path.Combine(_tempDir, ModelDefinition.Default.ProjectorFileName), "projector");
        Assert.True(svc.IsModelReady);

        // Switch to a model whose files don't exist
        var otherModel = new ModelDefinition("other", "Other", "other.gguf",
            "https://example.com/other.gguf", null, null);
        svc.SelectModel(otherModel);
        Assert.False(svc.IsModelReady);
    }

    [Fact]
    public void IsModelReady_WhenNoProjectorConfigured_ChecksOnlyMainFile()
    {
        var svc = new ModelDownloadService(_tempDir);

        var noProjector = new ModelDefinition("no-proj", "No Projector", "model.gguf",
            "https://example.com/model.gguf", null, null);
        svc.SelectModel(noProjector);

        // Only main file exists, no projector
        File.WriteAllText(Path.Combine(_tempDir, "model.gguf"), "model");
        Assert.True(svc.IsModelReady);
    }

    [Fact]
    public void SelectModel_SelectedModelReflectsNewValue()
    {
        var svc = new ModelDownloadService(_tempDir);
        Assert.Equal(ModelDefinition.Default, svc.SelectedModel);

        var newModel = new ModelDefinition("test-id", "Test", "test.gguf",
            "https://example.com/test.gguf", null, null);
        svc.SelectModel(newModel);

        Assert.Equal(newModel, svc.SelectedModel);
    }

    // ---- Helpers ----

    private class NoOpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([])
            });
    }

    private class RoutingHandler(byte[] modelBytes, byte[] projectorBytes, Action<HttpRequestMessage>? capture = null)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            capture?.Invoke(req);
            var isProjector = req.RequestUri?.AbsoluteUri.Contains("mmproj", StringComparison.OrdinalIgnoreCase) == true;
            return Task.FromResult(new HttpResponseMessage(
                req.Headers.Range is null ? HttpStatusCode.OK : HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(isProjector ? projectorBytes : modelBytes)
            });
        }
    }

    // Returns a real HTTP 200 response whose content stream writes firstChunk,
    // then cancels before writing the second chunk — ensuring .part is written
    // before the OperationCanceledException propagates.
    private class CancellingHandler(CancellationTokenSource cts, byte[] firstChunk) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var stream = new CancelAfterFirstChunkStream(cts, firstChunk);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream)
            };
            response.Content.Headers.ContentLength = firstChunk.Length + 4L; // pretend more to come
            return Task.FromResult(response);
        }
    }

    private class CancelAfterFirstChunkStream(CancellationTokenSource cts, byte[] chunk) : Stream
    {
        private bool _chunkSent;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (_chunkSent)
            {
                // Cancel after writing the first chunk so DownloadAsync has already written to .part
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
            }
            _chunkSent = true;
            var n = Math.Min(count, chunk.Length);
            Array.Copy(chunk, 0, buffer, offset, n);
            return n;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
