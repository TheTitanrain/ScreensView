namespace ScreensView.Viewer.Models;

public record LlmCheckResult(
    bool IsMatch,
    string Explanation,
    bool IsError,
    DateTime CheckedAt
);
