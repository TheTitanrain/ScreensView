namespace ScreensView.Viewer.Models;

public record ModelDefinition(
    string Id,
    string DisplayName,
    string FileName,
    string DownloadUrl,
    string? ProjectorFileName,
    string? ProjectorDownloadUrl)
{
    /// <summary>Stable id of the default model — independent of list position.</summary>
    public const string DefaultModelId = "llava-v1.5-7b-q4";

    /// <summary>The compiled-in catalog. Always the fallback when no user file is present.</summary>
    public static IReadOnlyList<ModelDefinition> BuiltIn { get; } =
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
        new("qwen3.5-9b-q4", "Qwen3.5-9B Q4_K_M (~5.7 + 0.9 GB)",
            "Qwen3.5-9B-Q4_K_M.gguf",
            "https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/main/Qwen3.5-9B-Q4_K_M.gguf",
            "Qwen3.5-9B-mmproj-F16.gguf",
            "https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/main/mmproj-F16.gguf"),
        new("qwopus3.5-9b-coder-q4", "Qwopus3.5-9B Coder Q4_K_M (~5.6 + 0.9 GB) [experimental]",
            "Qwopus3.5-9B-coder-Exp-Q4_K_M.gguf",
            "https://huggingface.co/Jackrong/Qwopus3.5-9B-Coder-GGUF/resolve/main/Qwopus3.5-9B-coder-Exp-Q4_K_M.gguf",
            "Qwopus3.5-9B-mmproj-F32.gguf",
            "https://huggingface.co/Jackrong/Qwopus3.5-9B-Coder-GGUF/resolve/main/mmproj-F32.gguf"),
    ];

    private static IReadOnlyList<ModelDefinition> _available = BuiltIn;
    private static bool _initialized;

    /// <summary>
    /// The active catalog shown in the UI. Defaults to <see cref="BuiltIn"/> until
    /// <see cref="Initialize"/> is called once at startup. Getter-only — production code
    /// cannot swap the list at runtime.
    /// </summary>
    public static IReadOnlyList<ModelDefinition> Available => _available;

    /// <summary>
    /// One-time catalog initialization. Idempotent — the first call wins, later calls are ignored.
    /// Called from <c>App.OnStartup</c> before any view model or window is constructed.
    /// </summary>
    internal static void Initialize(IReadOnlyList<ModelDefinition> catalog)
    {
        if (_initialized)
            return;

        _available = catalog is { Count: > 0 } ? catalog : BuiltIn;
        _initialized = true;
    }

    /// <summary>Test-only: restore the un-initialized state so static reads don't leak between tests.</summary>
    internal static void ResetForTests()
    {
        _available = BuiltIn;
        _initialized = false;
    }

    /// <summary>The default model, pinned by <see cref="DefaultModelId"/>, falling back to the first entry.</summary>
    public static ModelDefinition Default =>
        Available.FirstOrDefault(m => m.Id == DefaultModelId) ?? Available[0];
}
