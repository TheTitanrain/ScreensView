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

    [Fact]
    public void CheckInstallation_WhenVariantDirectoryIsMissing_ReturnsMissing()
    {
        var sut = new LlamaServerBinaryService(new FakeReleaseHandler("{}", new Dictionary<string, byte[]>()), _tempDir);

        var result = sut.CheckInstallation("cpu");

        Assert.Equal(BackendInstallState.Missing, result.State);
        Assert.Equal("cpu", result.Backend);
        Assert.Contains("Скачать бэкенд", result.UserMessage);
    }

    [Fact]
    public void CheckInstallation_WhenOnlyVersionFileExists_ReturnsIncomplete()
    {
        WriteVariantFiles("cpu", ("version.txt", "b1"));
        var sut = new LlamaServerBinaryService(new FakeReleaseHandler("{}", new Dictionary<string, byte[]>()), _tempDir);

        var result = sut.CheckInstallation("cpu");

        Assert.Equal(BackendInstallState.Incomplete, result.State);
        Assert.Contains("llama-server.exe", result.MissingArtifacts);
    }

    [Fact]
    public void CheckInstallation_WhenBaseDllIsMissing_ReturnsIncomplete()
    {
        WriteVariantFiles("cpu",
            ("version.txt", "b1"),
            ("llama-server.exe", "exe"),
            ("llama.dll", "dll"),
            ("ggml.dll", "ggml"));
        var sut = new LlamaServerBinaryService(new FakeReleaseHandler("{}", new Dictionary<string, byte[]>()), _tempDir);

        var result = sut.CheckInstallation("cpu");

        Assert.Equal(BackendInstallState.Incomplete, result.State);
        Assert.Contains("ggml-base.dll", result.MissingArtifacts);
    }

    [Fact]
    public void CheckInstallation_WhenCudaOnlyHasRuntimeDllsAndVersion_ReturnsIncomplete()
    {
        WriteVariantFiles("cuda",
            ("version.txt", "b1"),
            ("cudart64_12.dll", "cudart"),
            ("cublas64_12.dll", "cublas"),
            ("cublasLt64_12.dll", "cublaslt"));
        var sut = new LlamaServerBinaryService(new FakeReleaseHandler("{}", new Dictionary<string, byte[]>()), _tempDir);

        var result = sut.CheckInstallation("cuda");

        Assert.Equal(BackendInstallState.Incomplete, result.State);
        Assert.Contains("llama-server.exe", result.MissingArtifacts);
    }

    [Fact]
    public void CheckInstallation_WhenRequiredFileIsZeroBytes_ReturnsIncomplete()
    {
        WriteVariantFiles("cpu",
            ("version.txt", "b1"),
            ("llama-server.exe", string.Empty),
            ("llama.dll", "dll"),
            ("ggml.dll", "ggml"),
            ("ggml-base.dll", "base"));
        var sut = new LlamaServerBinaryService(new FakeReleaseHandler("{}", new Dictionary<string, byte[]>()), _tempDir);

        var result = sut.CheckInstallation("cpu");

        Assert.Equal(BackendInstallState.Incomplete, result.State);
        Assert.Contains("llama-server.exe", result.MissingArtifacts);
    }

    [Fact]
    public void CheckInstallation_WhenAllRequiredCpuFilesExist_ReturnsReady()
    {
        WriteVariantFiles("cpu",
            ("version.txt", "b1"),
            ("llama-server.exe", "exe"),
            ("llama.dll", "dll"),
            ("ggml.dll", "ggml"),
            ("ggml-base.dll", "base"));
        var sut = new LlamaServerBinaryService(new FakeReleaseHandler("{}", new Dictionary<string, byte[]>()), _tempDir);

        var result = sut.CheckInstallation("cpu");

        Assert.Equal(BackendInstallState.Ready, result.State);
        Assert.Equal("b1", result.InstalledVersion);
        Assert.Empty(result.MissingArtifacts);
    }

    [Fact]
    public void CheckInstallation_WhenAllRequiredCudaFilesExist_ReturnsReady()
    {
        WriteVariantFiles("cuda",
            ("version.txt", "b2"),
            ("llama-server.exe", "exe"),
            ("llama.dll", "dll"),
            ("ggml.dll", "ggml"),
            ("ggml-base.dll", "base"),
            ("cudart64_12.dll", "cudart"));
        var sut = new LlamaServerBinaryService(new FakeReleaseHandler("{}", new Dictionary<string, byte[]>()), _tempDir);

        var result = sut.CheckInstallation("cuda");

        Assert.Equal(BackendInstallState.Ready, result.State);
        Assert.Equal("b2", result.InstalledVersion);
    }

    private void WriteVariantFiles(string variant, params (string FileName, string Contents)[] files)
    {
        var variantDir = Path.Combine(_tempDir, variant);
        Directory.CreateDirectory(variantDir);

        foreach (var (fileName, contents) in files)
        {
            var path = Path.Combine(variantDir, fileName);
            File.WriteAllText(path, contents);
        }
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
