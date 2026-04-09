using System.IO;

namespace ScreensView.Viewer.Services;

internal sealed record LlamaServerLaunchProfile(
    int ParallelSlots,
    int ContextSize,
    int ReasoningBudget,
    int? ImageMaxTokens)
{
    public static LlamaServerLaunchProfile ForModel(string modelPath)
    {
        var fileName = Path.GetFileName(modelPath);
        var usesDynamicVisionTokens =
            fileName.Contains("qwen", StringComparison.OrdinalIgnoreCase);

        return new LlamaServerLaunchProfile(
            ParallelSlots: 1,
            ContextSize: 4096,
            ReasoningBudget: 0,
            ImageMaxTokens: usesDynamicVisionTokens ? 512 : null);
    }
}
