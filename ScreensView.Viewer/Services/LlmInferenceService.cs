using System.Windows.Media.Imaging;
using LLama;
using LLama.Common;
using ScreensView.Viewer.Models;

namespace ScreensView.Viewer.Services;

public interface ILlmInferenceService
{
    Task<LlmCheckResult> AnalyzeAsync(BitmapImage screenshot, string description, CancellationToken ct);
}

/// <summary>
/// LLamaSharp-based multimodal inference. Vision support is experimental —
/// verify LLavaWeights compatibility with Qwen3.5 GGUF before shipping.
/// See Task 11 in the implementation plan for details.
/// </summary>
public class LlmInferenceService : ILlmInferenceService, IDisposable
{
    private readonly IModelDownloadService _download;
    private LLamaWeights? _model;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public LlmInferenceService(IModelDownloadService download)
    {
        _download = download;
    }

    private async Task EnsureLoadedAsync()
    {
        if (_model is not null) return;
        await _loadLock.WaitAsync();
        try
        {
            if (_model is not null) return;
            var parameters = new ModelParams(_download.ModelPath) { ContextSize = 2048 };
            _model = LLamaWeights.LoadFromFile(parameters);
        }
        finally { _loadLock.Release(); }
    }

    public async Task<LlmCheckResult> AnalyzeAsync(
        BitmapImage screenshot, string description, CancellationToken ct)
    {
        try
        {
            await EnsureLoadedAsync();

            // Encode screenshot to JPEG bytes
            byte[] jpegBytes = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var encoder = new JpegBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(screenshot));
                using var ms = new System.IO.MemoryStream();
                encoder.Save(ms);
                return ms.ToArray();
            });

            // TODO: adapt to actual LLamaSharp vision API for the chosen model
            // See: https://github.com/SciSharp/LLamaSharp/tree/master/LLama.Examples
            // Verify LLavaWeights compatibility with Qwen3.5 GGUF before implementing.
            var prompt = $"Does the screen match this description: '{description}'? " +
                         "Reply with YES or NO and one sentence explanation.";

            // Placeholder — replace with actual LLamaSharp multimodal call:
            throw new NotImplementedException(
                "Replace this with LLamaSharp vision API call. See Task 11 step 2 notes.");
        }
        catch (OperationCanceledException)
        {
            throw; // propagate cancellation
        }
        catch (NotImplementedException)
        {
            throw; // surface stub
        }
        catch (Exception ex)
        {
            return new LlmCheckResult(false, ex.Message, IsError: true, DateTime.Now);
        }
    }

    public void Dispose()
    {
        _model?.Dispose();
        _loadLock.Dispose();
    }
}
