using System.Windows;
using System.Windows.Controls;
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

    private static T GetElement<T>(FrameworkElement root, string name) where T : FrameworkElement =>
        Assert.IsType<T>(root.FindName(name));

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
