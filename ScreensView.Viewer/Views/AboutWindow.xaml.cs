using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using ScreensView.Viewer.Services;

namespace ScreensView.Viewer.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        AppNameText.Text = "ScreensView";

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "неизвестна";
        VersionText.Text = $"Версия: {version}";

        CopyrightText.Text = "© 2025 titanrain";

        GitHubLink.NavigateUri = new Uri("https://github.com/titanrain/ScreensView");
        DonateLink.NavigateUri = new Uri("https://donatr.ee/titanrain");
    }

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        try
        {
            await ViewerUpdateService.CheckManualAsync(this);
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }
}
