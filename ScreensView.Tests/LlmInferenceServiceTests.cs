using ScreensView.Viewer.Models;
using ScreensView.Viewer.Services;

namespace ScreensView.Tests;

public class LlmInferenceServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_ParsesYesResponse_AndPassesImageToRuntime()
    {
        var runtime = new FakeVisionRuntime("YES Screen looks correct.");
        var factory = new FakeVisionRuntimeFactory(runtime);
        using var sut = new LlmInferenceService(new FakeModelDownloadService(), factory);

        var result = await sut.AnalyzeAsync(
            ComputerViewModelTests.CreateMinimalBitmap(),
            "Spreadsheet dashboard",
            CancellationToken.None);

        Assert.True(result.IsMatch);
        Assert.False(result.IsError);
        Assert.Equal("Screen looks correct.", result.Explanation);
        Assert.Contains("<image>", runtime.LastPrompt);
        Assert.Contains("Spreadsheet dashboard", runtime.LastPrompt);
        Assert.NotNull(runtime.LastImageBytes);
        Assert.NotEmpty(runtime.LastImageBytes!);
        Assert.Equal(@"C:\models\model.gguf", factory.ModelPathSeen);
        Assert.Equal(@"C:\models\mmproj.gguf", factory.ProjectorPathSeen);
    }

    [Fact]
    public async Task AnalyzeAsync_CachesLoadedRuntime()
    {
        var runtime = new FakeVisionRuntime("YES ok.");
        var factory = new FakeVisionRuntimeFactory(runtime);
        using var sut = new LlmInferenceService(new FakeModelDownloadService(), factory);
        var image = ComputerViewModelTests.CreateMinimalBitmap();

        await sut.AnalyzeAsync(image, "first", CancellationToken.None);
        await sut.AnalyzeAsync(image, "second", CancellationToken.None);

        Assert.Equal(1, factory.CreateCalls);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenResponseCannotBeParsed_ReturnsErrorResult()
    {
        var runtime = new FakeVisionRuntime("Maybe this matches.");
        var factory = new FakeVisionRuntimeFactory(runtime);
        using var sut = new LlmInferenceService(new FakeModelDownloadService(), factory);

        var result = await sut.AnalyzeAsync(
            ComputerViewModelTests.CreateMinimalBitmap(),
            "Browser with CRM",
            CancellationToken.None);

        Assert.False(result.IsMatch);
        Assert.True(result.IsError);
        Assert.Contains("YES or NO", result.Explanation);
    }

    private sealed class FakeModelDownloadService : IModelDownloadService
    {
        public bool IsModelReady => true;
        public string ModelPath => @"C:\models\model.gguf";
        public string ProjectorPath => @"C:\models\mmproj.gguf";
        public ModelDefinition SelectedModel { get; private set; } = ModelDefinition.Default;
        public void SelectModel(ModelDefinition model) => SelectedModel = model;
        public event EventHandler? ModelReady
        {
            add { }
            remove { }
        }

        public Task DownloadAsync(IProgress<double> progress, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class FakeVisionRuntimeFactory(FakeVisionRuntime runtime) : ILlmVisionRuntimeFactory
    {
        public int CreateCalls { get; private set; }
        public string? ModelPathSeen { get; private set; }
        public string? ProjectorPathSeen { get; private set; }

        public Task<ILlmVisionRuntime> CreateAsync(
            string modelPath,
            string projectorPath,
            CancellationToken ct)
        {
            CreateCalls++;
            ModelPathSeen = modelPath;
            ProjectorPathSeen = projectorPath;
            return Task.FromResult<ILlmVisionRuntime>(runtime);
        }
    }

    private sealed class FakeVisionRuntime(string response) : ILlmVisionRuntime
    {
        public byte[]? LastImageBytes { get; private set; }
        public string LastPrompt { get; private set; } = string.Empty;

        public Task<string> InferAsync(byte[] imageBytes, string prompt, CancellationToken ct)
        {
            LastImageBytes = imageBytes;
            LastPrompt = prompt;
            return Task.FromResult(response);
        }

        public void Dispose()
        {
        }
    }
}
