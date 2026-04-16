using System.Windows;
using ScreensView.Viewer.Services;

namespace ScreensView.Viewer.Views;

public partial class CredentialsDialog : Window
{
    public string? Username { get; private set; }
    public string? Password { get; private set; }

    public CredentialsDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (UseCurrentCredsBox.IsChecked != true)
                UsernameBox.Focus();
        };
    }

    private void CurrentCreds_Changed(object sender, RoutedEventArgs e)
    {
        if (ManualCredsPanel == null) return;

        ManualCredsPanel.Visibility = UseCurrentCredsBox.IsChecked == true
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (UseCurrentCredsBox.IsChecked != true)
            UsernameBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (UseCurrentCredsBox.IsChecked == true)
        {
            Username = null;
            Password = null;
            DialogResult = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(UsernameBox.Text))
        {
            MessageBox.Show(LocalizationService.Get("Str.Val.EnterUsername"), LocalizationService.Get("Str.Val.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            UsernameBox.Focus();
            return;
        }
        Username = UsernameBox.Text.Trim();
        Password = PasswordBox.Password;
        DialogResult = true;
    }
}
