namespace ScreensView.Shared;

public sealed class ScreenshotBusyException : InvalidOperationException
{
    public ScreenshotBusyException()
        : base("Screenshot capture is already in progress.")
    {
    }
}
