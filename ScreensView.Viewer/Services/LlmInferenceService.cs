using System.Windows.Media.Imaging;
using ScreensView.Viewer.Models;

namespace ScreensView.Viewer.Services;

public interface ILlmInferenceService
{
    Task<LlmCheckResult> AnalyzeAsync(BitmapImage screenshot, string description, CancellationToken ct);
}

/// <summary>
/// LLamaSharp-based multimodal inference. Vision support is experimental —
/// this stub throws until a compatible model/backend is confirmed.
/// See Task 11 in the implementation plan to complete this.
/// </summary>
public class LlmInferenceService : ILlmInferenceService, IDisposable
{
    private readonly IModelDownloadService _download;

    public LlmInferenceService(IModelDownloadService download)
    {
        _download = download;
    }

    public Task<LlmCheckResult> AnalyzeAsync(BitmapImage screenshot, string description, CancellationToken ct)
    {
        // TODO: implement LLamaSharp vision inference (Task 11)
        // Verify LLavaWeights compatibility with Qwen3.5 GGUF before implementing.
        throw new NotImplementedException("LlmInferenceService is not yet implemented. See Task 11.");
    }

    public void Dispose() { }
}
