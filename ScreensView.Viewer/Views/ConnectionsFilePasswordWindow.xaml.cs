using System.Windows;
using ScreensView.Viewer.Services;

namespace ScreensView.Viewer.Views;

public enum ConnectionsFilePasswordMode
{
    OpenExisting,
    CreateNew
}

public partial class ConnectionsFilePasswordWindow : Window
{
    private readonly ConnectionsFilePasswordMode _mode;

    public ConnectionsFilePasswordWindow(ConnectionsFilePasswordMode mode, string filePath, bool allowRememberPassword = true)
    {
        InitializeComponent();
        _mode = mode;

        TxtTitle.Text = mode == ConnectionsFilePasswordMode.CreateNew
            ? LocalizationService.Get("Str.Password.TitleCreate")
            : LocalizationService.Get("Str.Password.TitleOpen");
        TxtPath.Text = filePath;
        RememberPasswordHint.Text = mode == ConnectionsFilePasswordMode.CreateNew
            ? LocalizationService.Get("Str.Password.HintCreate")
            : LocalizationService.Get("Str.Password.HintOpen");
        ConfirmPanel.Visibility = mode == ConnectionsFilePasswordMode.CreateNew
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!allowRememberPassword)
        {
            RememberPasswordCheckBox.IsChecked = false;
            RememberPasswordCheckBox.Visibility = Visibility.Collapsed;
            RememberPasswordHint.Visibility = Visibility.Collapsed;
        }
    }

    public string Password { get; private set; } = string.Empty;

    public bool RememberPassword => RememberPasswordCheckBox.IsChecked == true;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordInput.Password;
        if (string.IsNullOrWhiteSpace(password))
        {
            MessageBox.Show(this, LocalizationService.Get("Str.Val.EnterPassword"), LocalizationService.Get("Str.Val.PasswordTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_mode == ConnectionsFilePasswordMode.CreateNew && password != ConfirmPasswordInput.Password)
        {
            MessageBox.Show(this, LocalizationService.Get("Str.Val.PasswordMismatch"), LocalizationService.Get("Str.Val.PasswordTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Password = password;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
