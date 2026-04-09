using System.Runtime.ExceptionServices;
using ScreensView.Viewer.Models;
using ScreensView.Viewer.Services;

namespace ScreensView.Tests;

public class LlmInferenceServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_DownscalesLargeScreenshotBeforeSendingToRuntime()
    {
        var runtime = new FakeVisionRuntime("YES Looks fine.");
        var factory = new FakeVisionRuntimeFactory(runtime);
        using var sut = new LlmInferenceService(new FakeModelDownloadService(), factory);

        var result = await sut.AnalyzeAsync(
            CreateBitmap(width: 2400, height: 1350),
            "Wallboard dashboard",
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(runtime.LastImageBytes);

        using var image = System.Drawing.Image.FromStream(new MemoryStream(runtime.LastImageBytes!));
        Assert.True(Math.Max(image.Width, image.Height) <= 1024);
    }

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
        Assert.Contains("Spreadsheet dashboard", runtime.LastPrompt);
        Assert.NotNull(runtime.LastImageBytes);
        Assert.NotEmpty(runtime.LastImageBytes!);
        Assert.Equal(@"C:\models\model.gguf", factory.ModelPathSeen);
        Assert.Equal(@"C:\models\mmproj.gguf", factory.ProjectorPathSeen);
    }

    [Fact]
    public async Task ValidateModelAsync_WhenRuntimeLoads_SucceedsAndCachesRuntime()
    {
        var runtime = new FakeVisionRuntime("YES ok.");
        var factory = new FakeVisionRuntimeFactory(runtime);
        using var sut = new LlmInferenceService(new FakeModelDownloadService(), factory);

        var validationError = await sut.ValidateModelAsync(CancellationToken.None);
        var result = await sut.AnalyzeAsync(
            ComputerViewModelTests.CreateMinimalBitmap(),
            "desc",
            CancellationToken.None);

        Assert.Null(validationError);
        Assert.False(result.IsError);
        Assert.Equal(1, factory.CreateCalls);
    }

    [Fact]
    public async Task ValidateModelAsync_WhenModelLoadFails_ReturnsModelLoadError()
    {
        var factory = new FakeVisionRuntimeFactory(
            new LlmRuntimeLoadException(
                LlmRuntimeLoadStage.ModelLoad,
                "Ошибка загрузки модели",
                "Failed to load model",
                @"C:\models\model.gguf",
                @"C:\models\mmproj.gguf"));
        using var sut = new LlmInferenceService(new FakeModelDownloadService(), factory);

        var error = await sut.ValidateModelAsync(CancellationToken.None);

        Assert.NotNull(error);
        Assert.Equal(LlmRuntimeLoadStage.ModelLoad, error.Stage);
        Assert.Equal("Ошибка загрузки модели", error.UserMessage);
        Assert.Equal(@"C:\models\model.gguf", error.ModelPath);
    }

    [Fact]
    public async Task ValidateModelAsync_WhenProjectorLoadFails_ReturnsProjectorLoadError()
    {
        var factory = new FakeVisionRuntimeFactory(
            new LlmRuntimeLoadException(
                LlmRuntimeLoadStage.ProjectorLoad,
                "Ошибка загрузки projector",
                "Failed to load projector",
                @"C:\models\model.gguf",
                @"C:\models\mmproj.gguf"));
        using var sut = new LlmInferenceService(new FakeModelDownloadService(), factory);

        var error = await sut.ValidateModelAsync(CancellationToken.None);

        Assert.NotNull(error);
        Assert.Equal(LlmRuntimeLoadStage.ProjectorLoad, error.Stage);
        Assert.Equal("Ошибка загрузки projector", error.UserMessage);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenParseFails_ReturnsParseErrorButDoesNotPretendModelLoadFailure()
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
        Assert.DoesNotContain("load model", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetUserMessage_ReturnsStageBasedMessage()
    {
        Assert.Equal("Бэкенд распознавания",
            LlmLoadFailureDiagnostics.GetUserMessage(LlmRuntimeLoadStage.Backend));
        Assert.Equal("Ошибка загрузки модели",
            LlmLoadFailureDiagnostics.GetUserMessage(LlmRuntimeLoadStage.ModelLoad));
        Assert.Equal("Ошибка загрузки projector",
            LlmLoadFailureDiagnostics.GetUserMessage(LlmRuntimeLoadStage.ProjectorLoad));
        Assert.Equal("Ошибка инициализации LLM runtime",
            LlmLoadFailureDiagnostics.GetUserMessage(LlmRuntimeLoadStage.RuntimeInit));
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

    private sealed class FakeVisionRuntimeFactory : ILlmVisionRuntimeFactory
    {
        private readonly FakeVisionRuntime? _runtime;
        private readonly LlmRuntimeLoadException? _loadError;

        public FakeVisionRuntimeFactory(FakeVisionRuntime runtime)
        {
            _runtime = runtime;
        }

        public FakeVisionRuntimeFactory(LlmRuntimeLoadException loadError)
        {
            _loadError = loadError;
        }

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

            if (_loadError is not null)
                throw _loadError;

            return Task.FromResult<ILlmVisionRuntime>(_runtime!);
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

    private static System.Windows.Media.Imaging.BitmapImage CreateBitmap(int width, int height)
    {
        using var bmp = new System.Drawing.Bitmap(width, height);
        using var graphics = System.Drawing.Graphics.FromImage(bmp);
        graphics.Clear(System.Drawing.Color.DarkSlateBlue);
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
        ms.Position = 0;

        return RunOnSta(() =>
        {
            var img = new System.Windows.Media.Imaging.BitmapImage();
            img.BeginInit();
            img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            img.StreamSource = new MemoryStream(ms.ToArray());
            img.EndInit();
            img.Freeze();
            return img;
        });
    }

    private static T RunOnSta<T>(Func<T> func)
    {
        T? result = default;
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught != null) ExceptionDispatchInfo.Capture(caught).Throw();
        return result!;
    }
}
