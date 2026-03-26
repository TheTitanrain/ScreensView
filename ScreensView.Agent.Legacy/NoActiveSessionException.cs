namespace ScreensView.Agent.Legacy;

internal sealed class NoActiveSessionException : InvalidOperationException
{
    public NoActiveSessionException()
        : base("No active console session — nobody is logged in at the console.") { }
}
