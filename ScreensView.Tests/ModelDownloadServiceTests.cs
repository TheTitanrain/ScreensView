using System.Net;
using System.Net.Http;
using System.Text;
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

    // ---- IsModelReady ----

    [Fact]
    public void IsModelReady_WhenNoFilesExist_ReturnsFalse()
    {
        Assert.False(Make().IsModelReady);
    }

    [Fact]
    public void IsModelReady_WhenOnlyPartFileExists_ReturnsFalse()
    {
        File.WriteAllText(Path.Combine(_tempDir, "qwen3.5-2b-q4_k_m.gguf.part"), "partial");
        Assert.False(Make().IsModelReady);
    }

    [Fact]
    public void IsModelReady_WhenFinalFileExistsAndNoPartFile_ReturnsTrue()
    {
        File.WriteAllText(Path.Combine(_tempDir, "qwen3.5-2b-q4_k_m.gguf"), "model");
        Assert.True(Make().IsModelReady);
    }

    [Fact]
    public void IsModelReady_WhenBothFilesExist_ReturnsFalse()
    {
        File.WriteAllText(Path.Combine(_tempDir, "qwen3.5-2b-q4_k_m.gguf"), "model");
        File.WriteAllText(Path.Combine(_tempDir, "qwen3.5-2b-q4_k_m.gguf.part"), "partial");
        Assert.False(Make().IsModelReady);
    }

    // ---- Download writes file ----

    [Fact]
    public async Task DownloadAsync_OnSuccess_WritesFileAndFiresModelReady()
    {
        var content = "fake-model-bytes"u8.ToArray();
        var handler = new FakeHandler(HttpStatusCode.OK, content);
        var svc = Make(handler);
        bool modelReadyFired = false;
        svc.ModelReady += (_, _) => modelReadyFired = true;

        await svc.DownloadAsync(new Progress<double>(), CancellationToken.None);

        Assert.True(svc.IsModelReady);
        Assert.True(modelReadyFired);
        Assert.False(File.Exists(Path.Combine(_tempDir, "qwen3.5-2b-q4_k_m.gguf.part")));
    }

    // ---- Resume via Range header ----

    [Fact]
    public async Task DownloadAsync_WhenPartFileExists_SendsRangeHeader()
    {
        var existingBytes = "existing"u8.ToArray();
        File.WriteAllBytes(Path.Combine(_tempDir, "qwen3.5-2b-q4_k_m.gguf.part"), existingBytes);

        string? rangeHeader = null;
        var remaining = "more"u8.ToArray();
        var handler = new CapturingHandler(HttpStatusCode.PartialContent, remaining,
            req => rangeHeader = req.Headers.Range?.ToString());
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

        Assert.True(File.Exists(Path.Combine(_tempDir, "qwen3.5-2b-q4_k_m.gguf.part")));
        Assert.False(svc.IsModelReady);
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

    private class FakeHandler(HttpStatusCode status, byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(content)
            });
    }

    private class CapturingHandler(HttpStatusCode status, byte[] content, Action<HttpRequestMessage> capture)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            capture(req);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(content)
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
