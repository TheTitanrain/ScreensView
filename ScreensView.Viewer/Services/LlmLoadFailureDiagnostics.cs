using ScreensView.Viewer.Models;

namespace ScreensView.Viewer.Services;

internal static class LlmLoadFailureDiagnostics
{
    public static string GetUserMessage(LlmRuntimeLoadStage stage) => stage switch
    {
        LlmRuntimeLoadStage.ModelLoad     => "Ошибка загрузки модели",
        LlmRuntimeLoadStage.ProjectorLoad => "Ошибка загрузки projector",
        _                                 => "Ошибка инициализации LLM runtime"
    };

    public static string GetDiagnosticMessage(string baseMessage, string? extra) =>
        string.IsNullOrWhiteSpace(extra) ? baseMessage : $"{baseMessage} {extra}";
}
