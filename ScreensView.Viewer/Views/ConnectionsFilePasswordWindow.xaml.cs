using System.Windows;

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
            ? "Создание зашифрованного файла подключений"
            : "Открытие зашифрованного файла подключений";
        TxtPath.Text = filePath;
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
            MessageBox.Show(this, "Введите пароль.", "Пароль", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_mode == ConnectionsFilePasswordMode.CreateNew && password != ConfirmPasswordInput.Password)
        {
            MessageBox.Show(this, "Подтверждение пароля не совпадает.", "Пароль", MessageBoxButton.OK, MessageBoxImage.Warning);
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
