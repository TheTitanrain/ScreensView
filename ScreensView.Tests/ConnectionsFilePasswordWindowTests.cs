using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ScreensView.Viewer.Views;

namespace ScreensView.Tests;

public sealed class ConnectionsFilePasswordWindowTests
{
    [Fact]
    public void TemporaryOpenMode_HidesRememberPasswordControls()
    {
        var snapshot = RunOnSta(() =>
        {
            var window = new ConnectionsFilePasswordWindow(
                ConnectionsFilePasswordMode.OpenExisting,
                @"C:\Shared\connections.svc",
                allowRememberPassword: false);

            return new
            {
                CheckBoxVisibility = GetElement<CheckBox>(window, "RememberPasswordCheckBox").Visibility,
                HintVisibility = GetElement<TextBlock>(window, "RememberPasswordHint").Visibility
            };
        });

        Assert.Equal(Visibility.Collapsed, snapshot.CheckBoxVisibility);
        Assert.Equal(Visibility.Collapsed, snapshot.HintVisibility);
    }

    [Fact]
    public void PersistentOpenMode_ShowsRememberPasswordControls()
    {
        var snapshot = RunOnSta(() =>
        {
            var window = new ConnectionsFilePasswordWindow(
                ConnectionsFilePasswordMode.OpenExisting,
                @"C:\Shared\connections.svc",
                allowRememberPassword: true);

            return new
            {
                CheckBoxVisibility = GetElement<CheckBox>(window, "RememberPasswordCheckBox").Visibility,
                HintVisibility = GetElement<TextBlock>(window, "RememberPasswordHint").Visibility
            };
        });

        Assert.Equal(Visibility.Visible, snapshot.CheckBoxVisibility);
        Assert.Equal(Visibility.Visible, snapshot.HintVisibility);
    }

    [Fact]
    public void CreateMode_ShowsEncryptedRememberPasswordHint()
    {
        var hintText = RunOnSta(() =>
        {
            var window = new ConnectionsFilePasswordWindow(
                ConnectionsFilePasswordMode.CreateNew,
                @"C:\Shared\connections.svc",
                allowRememberPassword: true);

            return GetElement<TextBlock>(window, "RememberPasswordHint").Text;
        });

        Assert.Contains("зашифрован", hintText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateMode_WithLongPath_KeepsActionButtonsInsideVisibleArea()
    {
        var snapshot = RunOnSta(() =>
        {
            var filePath = $@"C:\{string.Join(@"\", Enumerable.Repeat("very-long-folder-name", 14))}\connections-storage-file-with-a-very-long-name.svc";
            var window = new ConnectionsFilePasswordWindow(
                ConnectionsFilePasswordMode.CreateNew,
                filePath,
                allowRememberPassword: true)
            {
                FontSize = 18,
                ShowInTaskbar = false,
                Left = -10000,
                Top = -10000
            };

            try
            {
                window.Show();
                window.UpdateLayout();

                var root = Assert.IsType<Grid>(window.Content);
                var okButton = FindDefaultButton(window);
                var cancelButton = FindSecondaryActionButton(window);
                var handle = new WindowInteropHelper(window).Handle;
                Assert.NotEqual(IntPtr.Zero, handle);
                Assert.True(GetClientRect(handle, out var clientRect));

                return new
                {
                    ClientHeight = clientRect.Bottom - clientRect.Top,
                    OkBottom = okButton.TranslatePoint(new Point(0, okButton.ActualHeight), root).Y,
                    CancelBottom = cancelButton.TranslatePoint(new Point(0, cancelButton.ActualHeight), root).Y
                };
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(snapshot.ClientHeight > 0);
        Assert.InRange(snapshot.OkBottom, 0, snapshot.ClientHeight);
        Assert.InRange(snapshot.CancelBottom, 0, snapshot.ClientHeight);
    }

    private static T GetElement<T>(FrameworkElement root, string name) where T : FrameworkElement =>
        Assert.IsType<T>(root.FindName(name));

    private static Button FindDefaultButton(DependencyObject root)
    {
        var button = FindVisualDescendant<Button>(root, candidate =>
            candidate.IsDefault);

        return Assert.IsType<Button>(button);
    }

    private static Button FindSecondaryActionButton(DependencyObject root)
    {
        var button = FindVisualDescendant<Button>(root, candidate =>
            !candidate.IsDefault);

        return Assert.IsType<Button>(button);
    }

    private static T? FindVisualDescendant<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typedChild && predicate(typedChild))
                return typedChild;

            var nested = FindVisualDescendant(child, predicate);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    private static T RunOnSta<T>(Func<T> func)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
            throw error;

        return result!;
    }
}
