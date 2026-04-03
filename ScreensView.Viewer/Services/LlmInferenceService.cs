using System.Windows.Media.Imaging;
using LLama;
using LLama.Common;
using LLama.Sampling;
using ScreensView.Viewer.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace ScreensView.Viewer.Services;

public interface ILlmInferenceService
{
    Task<LlmCheckResult> AnalyzeAsync(BitmapImage screenshot, string description, CancellationToken ct);
}

internal interface ILlmVisionRuntime : IDisposable
{
    Task<string> InferAsync(byte[] imageBytes, string prompt, CancellationToken ct);
}

internal interface ILlmVisionRuntimeFactory
{
    Task<ILlmVisionRuntime> CreateAsync(string modelPath, string projectorPath, CancellationToken ct);
}

/// <summary>
/// LLamaSharp-based multimodal inference. Vision support is experimental —
/// verify LLavaWeights compatibility with Qwen3.5 GGUF before shipping.
/// See Task 11 in the implementation plan for details.
/// </summary>
public class LlmInferenceService : ILlmInferenceService, IDisposable
{
    private readonly IModelDownloadService _download;
    private readonly ILlmVisionRuntimeFactory _runtimeFactory;
    private ILlmVisionRuntime? _runtime;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public LlmInferenceService(IModelDownloadService download)
        : this(download, new LLamaSharpVisionRuntimeFactory())
    {
    }

    internal LlmInferenceService(IModelDownloadService download, ILlmVisionRuntimeFactory runtimeFactory)
    {
        _download = download;
        _runtimeFactory = runtimeFactory;
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_runtime is not null)
            return;

        await _loadLock.WaitAsync(ct);
        try
        {
            if (_runtime is not null)
                return;

            _runtime = await _runtimeFactory.CreateAsync(
                _download.ModelPath,
                _download.ProjectorPath,
                ct);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public async Task<LlmCheckResult> AnalyzeAsync(
        BitmapImage screenshot, string description, CancellationToken ct)
    {
        try
        {
            await EnsureLoadedAsync(ct);

            var jpegBytes = await EncodeJpegAsync(screenshot);
            var prompt = BuildPrompt(description);
            var rawResponse = await _runtime!.InferAsync(jpegBytes, prompt, ct);
            var (isMatch, explanation) = ParseModelResponse(rawResponse);

            return new LlmCheckResult(isMatch, explanation, IsError: false, DateTime.Now);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new LlmCheckResult(false, ex.Message, IsError: true, DateTime.Now);
        }
    }

    public void Dispose()
    {
        _runtime?.Dispose();
        _loadLock.Dispose();
    }

    private static Task<byte[]> EncodeJpegAsync(BitmapSource screenshot)
    {
        if (screenshot.IsFrozen || screenshot.Dispatcher.CheckAccess())
            return Task.FromResult(EncodeJpeg(screenshot));

        return screenshot.Dispatcher.InvokeAsync(() => EncodeJpeg(screenshot)).Task;
    }

    private static byte[] EncodeJpeg(BitmapSource screenshot)
    {
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(screenshot));
        using var ms = new System.IO.MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static string BuildPrompt(string description)
    {
        return $"<image>\nUSER:\nDoes the screen match this description: '{description}'? " +
               "Reply with YES or NO and one sentence explanation.\nASSISTANT:\n";
    }

    private static (bool IsMatch, string Explanation) ParseModelResponse(string rawResponse)
    {
        var normalized = rawResponse.Trim();
        if (normalized.StartsWith("ASSISTANT:", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["ASSISTANT:".Length..].Trim();

        normalized = Regex.Replace(
            normalized,
            @"\[\s*end of text\s*\]\s*$",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

        var match = Regex.Match(
            normalized,
            @"^(YES|NO)\b[\s:,-]*(.*)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

        if (!match.Success)
            throw new FormatException("Model response did not start with YES or NO.");

        var isMatch = string.Equals(match.Groups[1].Value, "YES", StringComparison.OrdinalIgnoreCase);
        var explanation = match.Groups[2].Value.Trim();
        if (string.IsNullOrWhiteSpace(explanation))
            explanation = "No explanation provided.";

        return (isMatch, explanation);
    }
}

internal sealed class LLamaSharpVisionRuntimeFactory : ILlmVisionRuntimeFactory
{
    public async Task<ILlmVisionRuntime> CreateAsync(string modelPath, string projectorPath, CancellationToken ct)
    {
        var parameters = new ModelParams(modelPath)
        {
            ContextSize = 4096
        };
        var model = await LLamaWeights.LoadFromFileAsync(parameters, ct);
        var projector = await LLavaWeights.LoadFromFileAsync(projectorPath, ct);
        return new LLamaSharpVisionRuntime(parameters, model, projector);
    }
}

internal sealed class LLamaSharpVisionRuntime : ILlmVisionRuntime
{
    private readonly ModelParams _parameters;
    private readonly LLamaWeights _model;
    private readonly LLavaWeights _projector;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);

    public LLamaSharpVisionRuntime(ModelParams parameters, LLamaWeights model, LLavaWeights projector)
    {
        _parameters = parameters;
        _model = model;
        _projector = projector;
    }

    public async Task<string> InferAsync(byte[] imageBytes, string prompt, CancellationToken ct)
    {
        await _inferenceLock.WaitAsync(ct);
        try
        {
            using var context = _model.CreateContext(_parameters);
            var executor = new InteractiveExecutor(context, _projector);
            executor.Images.Add(imageBytes);

            var inferenceParams = new InferenceParams
            {
                MaxTokens = 128,
                AntiPrompts = new List<string> { "\nUSER:" },
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = 0.1f
                }
            };

            var builder = new StringBuilder();
            await foreach (var chunk in executor.InferAsync(prompt, inferenceParams, ct))
                builder.Append(chunk);

            return builder.ToString();
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    public void Dispose()
    {
        _projector.Dispose();
        _model.Dispose();
        _inferenceLock.Dispose();
    }
}
