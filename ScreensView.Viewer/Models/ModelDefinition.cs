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
        new("llava-v1.5-7b-q4", "LLaVA v1.5 7B Q4_K_M (~4.1 + 0.6 GB)",
            "llava-v1.5-7b-Q4_K_M.gguf",
            "https://huggingface.co/second-state/Llava-v1.5-7B-GGUF/resolve/main/llava-v1.5-7b-Q4_K_M.gguf",
            "llava-v1.5-7b-mmproj-model-f16.gguf",
            "https://huggingface.co/second-state/Llava-v1.5-7B-GGUF/resolve/main/llava-v1.5-7b-mmproj-model-f16.gguf"),
        new("qwen3.5-2b-q4", "Qwen3.5-2B Q4_K_M (~1.3 + 0.6 GB)",
            "Qwen3.5-2B-Q4_K_M.gguf",
            "https://huggingface.co/unsloth/Qwen3.5-2B-GGUF/resolve/main/Qwen3.5-2B-Q4_K_M.gguf",
            "mmproj-F16.gguf",
            "https://huggingface.co/unsloth/Qwen3.5-2B-GGUF/resolve/main/mmproj-F16.gguf"),
    ];

    public static ModelDefinition Default => Available[0];
}
