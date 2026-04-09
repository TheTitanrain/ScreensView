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
        new("gemma-4-e2b-q4", "Gemma 4 E2B Q4_K_M (~3.0 + 0.6 GB) [experimental]",
            "gemma-4-E2B-it-Q4_K_M.gguf",
            "https://huggingface.co/unsloth/gemma-4-E2B-it-GGUF/resolve/main/gemma-4-E2B-it-Q4_K_M.gguf",
            "gemma-4-E2B-it-mmproj-F16.gguf",
            "https://huggingface.co/unsloth/gemma-4-E2B-it-GGUF/resolve/main/mmproj-F16.gguf"),
        new("qwen3.5-2b-q4", "Qwen3.5-2B Q4_K_M (~1.3 + 0.6 GB)",
            "Qwen3.5-2B-Q4_K_M.gguf",
            "https://huggingface.co/unsloth/Qwen3.5-2B-GGUF/resolve/main/Qwen3.5-2B-Q4_K_M.gguf",
            "Qwen3.5-2B-mmproj-F16.gguf",
            "https://huggingface.co/unsloth/Qwen3.5-2B-GGUF/resolve/main/mmproj-F16.gguf"),
        new("qwen3.5-0.8b-q4", "Qwen3.5-0.8B Q4_K_M (~0.5 + 0.2 GB) [experimental]",
            "Qwen3.5-0.8B-Q4_K_M.gguf",
            "https://huggingface.co/unsloth/Qwen3.5-0.8B-GGUF/resolve/main/Qwen3.5-0.8B-Q4_K_M.gguf",
            "Qwen3.5-0.8B-mmproj-F16.gguf",
            "https://huggingface.co/unsloth/Qwen3.5-0.8B-GGUF/resolve/main/mmproj-F16.gguf"),
        new("qwen3-vl-2b-q4", "Qwen3-VL-2B-Instruct Q4_K_M (~1.1 + 0.8 GB) [experimental]",
            "Qwen3-VL-2B-Instruct-Q4_K_M.gguf",
            "https://huggingface.co/unsloth/Qwen3-VL-2B-Instruct-GGUF/resolve/main/Qwen3-VL-2B-Instruct-Q4_K_M.gguf",
            "qwen3-vl-2b-instruct-mmproj-F16.gguf",
            "https://huggingface.co/unsloth/Qwen3-VL-2B-Instruct-GGUF/resolve/main/mmproj-F16.gguf"),
    ];

    public static ModelDefinition Default => Available[0];
}
