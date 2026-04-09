using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using ScreensView.Viewer.Services;

namespace ScreensView.Tests;

public sealed class LlamaServerBinaryServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public LlamaServerBinaryServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task DownloadAsync_ForCuda_DownloadsMainArchiveAndCudaRuntimeDlls()
    {
        var releaseJson = """
            {
              "tag_name": "b8710",
              "assets": [
                {
                  "name": "llama-b8710-bin-win-cuda-12.4-x64.zip",
                  "browser_download_url": "https://example.test/llama-cuda.zip"
                },
                {
                  "name": "cudart-llama-bin-win-cuda-12.4-x64.zip",
                  "browser_download_url": "https://example.test/cudart.zip"
                }
              ]
            }
            """;

        var handler = new FakeReleaseHandler(
            releaseJson,
            new Dictionary<string, byte[]>
            {
                ["https://example.test/llama-cuda.zip"] = CreateZip(
                    ("llama-server.exe", "exe"),
                    ("ggml-base.dll", "ggml")),
                ["https://example.test/cudart.zip"] = CreateZip(
                    ("cudart64_12.dll", "cudart"),
                    ("cublas64_12.dll", "cublas"))
            });
        var sut = new LlamaServerBinaryService(handler, _tempDir);

        await sut.DownloadAsync("cuda", new Progress<double>(), CancellationToken.None);

        Assert.Contains("https://example.test/llama-cuda.zip", handler.RequestedUrls);
        Assert.Contains("https://example.test/cudart.zip", handler.RequestedUrls);
        Assert.True(File.Exists(Path.Combine(_tempDir, "cuda", "llama-server.exe")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "cuda", "cudart64_12.dll")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "cuda", "cublas64_12.dll")));
        Assert.Equal("b8710", File.ReadAllText(Path.Combine(_tempDir, "cuda", "version.txt")).Trim());
    }

    private static byte[] CreateZip(params (string FileName, string Contents)[] files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (fileName, contents) in files)
            {
                var entry = zip.CreateEntry(fileName);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);
                writer.Write(contents);
            }
        }

        return memory.ToArray();
    }

    private sealed class FakeReleaseHandler(string releaseJson, IReadOnlyDictionary<string, byte[]> assets)
        : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.AbsoluteUri ?? string.Empty;
            RequestedUrls.Add(url);

            if (url.Contains("/releases/latest", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                });
            }

            if (assets.TryGetValue(url, out var payload))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(payload)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
