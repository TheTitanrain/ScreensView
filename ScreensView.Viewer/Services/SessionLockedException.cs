namespace ScreensView.Viewer.Services;

internal sealed class SessionLockedException(string message) : Exception(message) { }
