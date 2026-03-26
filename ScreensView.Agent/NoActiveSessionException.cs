namespace ScreensView.Agent;

public sealed class NoActiveSessionException : InvalidOperationException
{
    public NoActiveSessionException()
        : base("No active console session — nobody is logged in at the console.") { }
}
