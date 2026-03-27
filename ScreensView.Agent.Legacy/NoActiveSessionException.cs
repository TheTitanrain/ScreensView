namespace ScreensView.Agent.Legacy;

internal sealed class NoActiveSessionException : InvalidOperationException
{
    private const string DefaultMessage = "No active console session — nobody is logged in at the console.";

    public NoActiveSessionException()
        : base(DefaultMessage) { }

    public NoActiveSessionException(string message)
        : base(string.IsNullOrWhiteSpace(message) ? DefaultMessage : message) { }
}
