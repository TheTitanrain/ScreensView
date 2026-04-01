using System.Windows;
using ScreensView.Shared;
using ScreensView.Shared.Models;
using ScreensView.Viewer.Services;

namespace ScreensView.Viewer.Views;

public partial class AddEditComputerWindow : Window
{
    public ComputerConfig? Result { get; private set; }

    private readonly ComputerConfig? _existing;

    public AddEditComputerWindow(ComputerConfig? existing)
    {
        InitializeComponent();
        _existing = existing;

        if (existing != null)
        {
            NameBox.Text = existing.Name;
            HostBox.Text = existing.Host;
            PortBox.Text = existing.Port.ToString();
            ApiKeyBox.Text = existing.ApiKey;
            EnabledCheck.IsChecked = existing.IsEnabled;
        }
        else
        {
            PortBox.Text = Constants.DefaultPort.ToString();
            ApiKeyBox.Text = GenerateApiKey();
        }
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        ApiKeyBox.Text = GenerateApiKey();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        { MessageBox.Show("Введите имя компьютера.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (string.IsNullOrWhiteSpace(HostBox.Text))
        { MessageBox.Show("Введите хост или IP-адрес.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (!int.TryParse(PortBox.Text, out int port) || port < 1 || port > 65535)
        { MessageBox.Show("Введите корректный порт (1–65535).", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (string.IsNullOrWhiteSpace(ApiKeyBox.Text))
        { MessageBox.Show("API-ключ не может быть пустым.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        var newHost = HostBox.Text.Trim();
        var certThumbprint = (_existing != null
            && string.Equals(_existing.Host.Trim(), newHost, StringComparison.OrdinalIgnoreCase)
            && _existing.Port == port)
            ? _existing.CertThumbprint
            : string.Empty;

        Result = new ComputerConfig
        {
            Name = NameBox.Text.Trim(),
            Host = newHost,
            Port = port,
            ApiKey = ApiKeyBox.Text.Trim(),
            IsEnabled = EnabledCheck.IsChecked == true,
            CertThumbprint = certThumbprint
        };
        DialogResult = true;
    }

    private static string GenerateApiKey() => BulkComputerParser.GenerateApiKey();
}
