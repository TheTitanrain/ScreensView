namespace ScreensView.Viewer.Models;

public enum LlmRuntimeLoadStage
{
    Backend,
    ModelLoad,
    ProjectorLoad,
    RuntimeInit
}

public sealed class LlmRuntimeLoadException : Exception
{
    public LlmRuntimeLoadException(
        LlmRuntimeLoadStage stage,
        string userMessage,
        string diagnosticMessage,
        string modelPath,
        string projectorPath,
        Exception? innerException = null)
        : base(diagnosticMessage, innerException)
    {
        Stage = stage;
        UserMessage = userMessage;
        DiagnosticMessage = diagnosticMessage;
        ModelPath = modelPath;
        ProjectorPath = projectorPath;
    }

    public LlmRuntimeLoadStage Stage { get; }
    public string UserMessage { get; }
    public string DiagnosticMessage { get; }
    public string ModelPath { get; }
    public string ProjectorPath { get; }
}
