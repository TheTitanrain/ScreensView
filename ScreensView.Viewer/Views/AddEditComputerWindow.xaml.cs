using System.Windows;
using ScreensView.Shared;
using ScreensView.Shared.Models;
using ScreensView.Viewer.Services;

namespace ScreensView.Viewer.Views;

public partial class AddEditComputerWindow : Window
{
    public ComputerConfig? Result { get; private set; }

    private readonly ComputerConfig? _existing;
    private readonly bool _isEditMode;

    public AddEditComputerWindow(ComputerConfig? existing)
    {
        InitializeComponent();
        _existing = existing;
        _isEditMode = existing is not null;

        ConfigureWindow();

        if (_isEditMode)
        {
            LoadExisting(existing!);
        }
        else
        {
            LoadDefaults();
        }
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (_isEditMode)
        {
            return;
        }

        ApiKeyBox.Text = GenerateApiKey();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var buildResult = BuildResultOrValidationMessage();
        if (buildResult is string validationMessage)
        {
            MessageBox.Show(validationMessage, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = (ComputerConfig)buildResult;
        DialogResult = true;
    }

    internal object BuildResultOrValidationMessage()
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            return "Введите имя компьютера.";
        }

        if (string.IsNullOrWhiteSpace(HostBox.Text))
        {
            return "Введите хост или IP-адрес.";
        }

        if (!int.TryParse(PortBox.Text, out var port) || port < 1 || port > 65535)
        {
            return "Введите корректный порт (1–65535).";
        }

        if (string.IsNullOrWhiteSpace(ApiKeyBox.Text))
        {
            return "API-ключ не может быть пустым.";
        }

        var newHost = HostBox.Text.Trim();
        var certThumbprint = (_existing is not null
            && string.Equals(_existing.Host.Trim(), newHost, StringComparison.OrdinalIgnoreCase)
            && _existing.Port == port)
            ? _existing.CertThumbprint
            : string.Empty;

        var description = DescriptionBox.Text.Trim();
        return new ComputerConfig
        {
            Name = NameBox.Text.Trim(),
            Host = newHost,
            Port = port,
            ApiKey = ApiKeyBox.Text.Trim(),
            IsEnabled = EnabledCheck.IsChecked == true,
            CertThumbprint = certThumbprint,
            Description = description.Length > 0 ? description : null
        };
    }

    private static string GenerateApiKey() => BulkComputerParser.GenerateApiKey();

    private void ConfigureWindow()
    {
        if (_isEditMode)
        {
            Title = "Редактировать компьютер";
            HeaderTitleText.Text = "Редактировать компьютер";
            HeaderDescriptionText.Text = "Обновите параметры подключения и описание экрана, не меняя общий сценарий работы списка компьютеров.";
            PrimaryActionButton.Content = "Сохранить";
            GenerateApiKeyButton.IsEnabled = false;
            return;
        }

        Title = "Добавить компьютер";
        HeaderTitleText.Text = "Добавить компьютер";
        HeaderDescriptionText.Text = "Заполните основные данные нового компьютера. Настройки сохраняются в том же формате, что и раньше.";
        PrimaryActionButton.Content = "Добавить";
        GenerateApiKeyButton.IsEnabled = true;
    }

    private void LoadExisting(ComputerConfig existing)
    {
        NameBox.Text = existing.Name;
        HostBox.Text = existing.Host;
        PortBox.Text = existing.Port.ToString();
        ApiKeyBox.Text = existing.ApiKey;
        EnabledCheck.IsChecked = existing.IsEnabled;
        DescriptionBox.Text = existing.Description ?? string.Empty;
    }

    private void LoadDefaults()
    {
        PortBox.Text = Constants.DefaultPort.ToString();
        ApiKeyBox.Text = GenerateApiKey();
        EnabledCheck.IsChecked = true;
    }
}
