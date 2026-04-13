using System.Runtime.InteropServices;
using ScreensView.Viewer.Services;

namespace ScreensView.Tests;

public sealed class ConnectionsSourceDialogsTests
{
    [Fact]
    public void ShowOpenExternalFileFailed_WithoutOwner_DoesNotThrow()
    {
        RunOnSta(() =>
        {
            using var closer = MessageBoxCloser.Start("Файл подключений");
            var dialogs = new ConnectionsSourceDialogs(() => null);

            var exception = Record.Exception(() => dialogs.ShowOpenExternalFileFailed(needsPassword: true));

            Assert.Null(exception);
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA test thread timed out.");

        if (error is not null)
            throw error;
    }

    private sealed class MessageBoxCloser : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _task;

        private MessageBoxCloser(string caption)
        {
            _task = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    var handle = FindWindow(lpClassName: null, lpWindowName: caption);
                    if (handle != IntPtr.Zero)
                    {
                        PostMessage(handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                        return;
                    }

                    await Task.Delay(50, _cts.Token);
                }
            }, _cts.Token);
        }

        public static MessageBoxCloser Start(string caption) => new(caption);

        public void Dispose()
        {
            _cts.Cancel();
            try
            {
                _task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }

            _cts.Dispose();
        }
    }

    private const uint WM_CLOSE = 0x0010;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
