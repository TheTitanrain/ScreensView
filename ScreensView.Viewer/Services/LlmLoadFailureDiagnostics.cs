using System.Text.RegularExpressions;
using ScreensView.Viewer.Models;

namespace ScreensView.Viewer.Services;

internal static partial class LlmLoadFailureDiagnostics
{
    public static string GetUserMessage(LlmRuntimeLoadStage stage, string? nativeSummary)
    {
        if (stage == LlmRuntimeLoadStage.ModelLoad
            && TryExtractUnknownArchitecture(nativeSummary, out var architecture))
        {
            return $"Текущая модель не поддерживается LLama runtime (архитектура {architecture}).";
        }

        return stage switch
        {
            LlmRuntimeLoadStage.ModelLoad => "Ошибка загрузки модели",
            LlmRuntimeLoadStage.ProjectorLoad => "Ошибка загрузки projector",
            _ => "Ошибка инициализации LLM runtime"
        };
    }

    public static string GetDiagnosticMessage(string baseMessage, string? nativeSummary)
    {
        return string.IsNullOrWhiteSpace(nativeSummary)
            ? baseMessage
            : $"{baseMessage} Native llama log: {nativeSummary}";
    }

    private static bool TryExtractUnknownArchitecture(string? nativeSummary, out string architecture)
    {
        architecture = string.Empty;
        if (string.IsNullOrWhiteSpace(nativeSummary))
            return false;

        var match = UnknownArchitectureRegex().Match(nativeSummary);
        if (!match.Success)
            return false;

        architecture = match.Groups["arch"].Value;
        return !string.IsNullOrWhiteSpace(architecture);
    }

    [GeneratedRegex(@"unknown model architecture:\s*'(?<arch>[^']+)'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnknownArchitectureRegex();
}
