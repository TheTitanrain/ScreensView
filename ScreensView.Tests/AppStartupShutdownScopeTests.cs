using System.Windows;
using ScreensView.Viewer;

namespace ScreensView.Tests;

public sealed class AppStartupShutdownScopeTests
{
    [Fact]
    public void StartupShutdownScope_SwitchesToExplicitShutdownAndRestoresPreviousMode()
    {
        var host = new FakeShutdownModeHost
        {
            ShutdownMode = ShutdownMode.OnLastWindowClose
        };

        using (var scope = new StartupShutdownScope(host))
        {
            Assert.Equal(ShutdownMode.OnExplicitShutdown, host.ShutdownMode);
        }

        Assert.Equal(ShutdownMode.OnLastWindowClose, host.ShutdownMode);
    }

    [Fact]
    public void StartupShutdownScope_DoesNotRestoreModeWhenShutdownAlreadyStarted()
    {
        var host = new FakeShutdownModeHost
        {
            ShutdownMode = ShutdownMode.OnLastWindowClose
        };

        using (var scope = new StartupShutdownScope(host))
        {
            host.IsShuttingDown = true;
            Assert.Equal(ShutdownMode.OnExplicitShutdown, host.ShutdownMode);
        }

        Assert.Equal(ShutdownMode.OnExplicitShutdown, host.ShutdownMode);
    }

    private sealed class FakeShutdownModeHost : IShutdownModeHost
    {
        public ShutdownMode ShutdownMode { get; set; }
        public bool IsShuttingDown { get; set; }
    }
}
