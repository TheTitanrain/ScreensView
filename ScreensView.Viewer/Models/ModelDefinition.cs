namespace ScreensView.Viewer.Models;

public record ModelDefinition(
    string Id,
    string DisplayName,
    string FileName,
    string DownloadUrl,
    string? ProjectorFileName,
    string? ProjectorDownloadUrl)
{
    public static IReadOnlyList<ModelDefinition> Available { get; } =
    [
        new("qwen3.5-2b-q4", "Qwen3.5-2B Q4_K_M (~1.3 + 0.6 GB)",
            "Qwen3.5-2B-Q4_K_M.gguf",
            "https://huggingface.co/unsloth/Qwen3.5-2B-GGUF/resolve/main/Qwen3.5-2B-Q4_K_M.gguf",
            "mmproj-F16.gguf",
            "https://huggingface.co/unsloth/Qwen3.5-2B-GGUF/resolve/main/mmproj-F16.gguf"),
    ];

    public static ModelDefinition Default => Available[0];
}
