using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ScreensView.Viewer.Helpers;
using ScreensView.Viewer.Services;

namespace ScreensView.Viewer.Views;

public partial class ComputersManagerWindow : Window
{
    private readonly ViewModels.MainViewModel _mainVm;
    private readonly ConnectionsStorageController _controller;
    private readonly IViewerSettingsService _settingsService;

    internal ComputersManagerWindow(
        ViewModels.MainViewModel mainVm,
        ConnectionsStorageController controller,
        IViewerSettingsService settingsService)
    {
        InitializeComponent();
        _mainVm = mainVm;
        _controller = controller;
        _settingsService = settingsService;
        ComputersList.ItemsSource = mainVm.Computers;
        RefreshConnectionsSourceUi();
    }

    private ViewModels.ComputerViewModel? Selected => ComputersList.SelectedItem as ViewModels.ComputerViewModel;

    private List<Shared.Models.ComputerConfig> SelectedConfigs =>
        ComputersList.SelectedItems.Cast<ViewModels.ComputerViewModel>()
            .Select(vm => vm.ToConfig()).ToList();

    private void ComputersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var count = ComputersList.SelectedItems.Count;
        BtnEdit.IsEnabled      = count == 1;
        BtnDelete.IsEnabled    = count >= 1;
        BtnInstall.IsEnabled   = count >= 1;
        BtnUninstall.IsEnabled = count >= 1;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var win = new AddEditComputerWindow(null) { Owner = this };
        if (win.ShowDialog() == true && win.Result != null)
            _mainVm.AddComputer(win.Result);
    }

    private void AddMultiple_Click(object sender, RoutedEventArgs e)
    {
        var existingHosts = _mainVm.Computers
            .Select(c => c.Host)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var win = new AddMultipleComputersWindow(existingHosts) { Owner = this };
        if (win.ShowDialog() != true || win.Results.Count == 0) return;

        var added = win.Results;
        _mainVm.AddComputers(added);

        if (MessageBox.Show($"Установить агент на {added.Count} добавленных компьютеров?",
                "Установка агента", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            LaunchInstall(added);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected == null) return;
        var win = new AddEditComputerWindow(Selected.ToConfig()) { Owner = this };
        if (win.ShowDialog() == true && win.Result != null)
            _mainVm.UpdateComputer(Selected, win.Result);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var selected = ComputersList.SelectedItems.Cast<ViewModels.ComputerViewModel>().ToList();
        if (selected.Count == 0) return;

        var message = selected.Count == 1
            ? $"Удалить компьютер '{selected[0].Name}'?"
            : $"Удалить компьютеры: {ComputerListHelpers.FormatNames(selected.Select(vm => vm.Name))}?";

        if (MessageBox.Show(message, "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _mainVm.RemoveComputers(selected);
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        var configs = SelectedConfigs;
        if (configs.Count == 0) return;
        LaunchOperation(InstallProgressWindow.Mode.Install, configs);
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var configs = SelectedConfigs;
        if (configs.Count == 0) return;

        var message = configs.Count == 1
            ? $"Удалить агент с '{configs[0].Name}'?"
            : $"Удалить агент с компьютеров: {ComputerListHelpers.FormatNames(configs.Select(c => c.Name))}?";

        if (MessageBox.Show(message, "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        LaunchOperation(InstallProgressWindow.Mode.Uninstall, configs);
    }

    private void LaunchInstall(List<Shared.Models.ComputerConfig> configs) =>
        LaunchOperation(InstallProgressWindow.Mode.Install, configs);

    private void LaunchOperation(InstallProgressWindow.Mode mode, List<Shared.Models.ComputerConfig> configs)
    {
        var creds = new CredentialsDialog { Owner = this };
        if (creds.ShowDialog() != true) return;
        new InstallProgressWindow(mode, configs, creds.Username, creds.Password) { Owner = this }.ShowDialog();
    }

    private void ConnectionsFile_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "Файл подключений будет доступен всем, у кого есть сам файл и пароль к нему. Храните его только в папке с ограниченным доступом.",
                "Внешний файл подключений",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        var settings = _settingsService.Load();
        var dialog = new SaveFileDialog
        {
            Title = "Выберите файл подключений",
            Filter = "Files (*.json;*.svc)|*.json;*.svc|All files (*.*)|*.*",
            AddExtension = true,
            OverwritePrompt = false,
            FileName = string.IsNullOrWhiteSpace(settings.ConnectionsFilePath)
                ? "connections.svc"
                : Path.GetFileName(settings.ConnectionsFilePath)
        };

        if (!string.IsNullOrWhiteSpace(settings.ConnectionsFilePath))
        {
            var directory = Path.GetDirectoryName(settings.ConnectionsFilePath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                dialog.InitialDirectory = directory;
        }

        if (dialog.ShowDialog(this) != true)
            return;

        if (File.Exists(dialog.FileName))
            OpenExistingConnectionsFile(dialog.FileName);
        else
            CreateNewConnectionsFile(dialog.FileName);
    }

    private void UseLocal_Click(object sender, RoutedEventArgs e)
    {
        var result = _controller.SwitchToLocalStorage(_mainVm.Computers.Select(item => item.ToConfig()).ToList());
        if (!result.Succeeded || result.Storage is null)
        {
            MessageBox.Show(
                this,
                "Не удалось переключиться на локальный файл подключений.",
                "Файл подключений",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        _mainVm.ApplyConnectionsSourceChange(result.Succeeded, result.Storage, result.Computers);
        RefreshConnectionsSourceUi();
    }

    private void OpenExistingConnectionsFile(string filePath)
    {
        while (true)
        {
            var dialog = new ConnectionsFilePasswordWindow(ConnectionsFilePasswordMode.OpenExisting, filePath) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            var result = _controller.OpenExternalFile(filePath, dialog.Password, dialog.RememberPassword);
            if (result.Succeeded && result.Storage is not null)
            {
                _mainVm.ApplyConnectionsSourceChange(true, result.Storage, result.Computers);
                RefreshConnectionsSourceUi();
                return;
            }

            MessageBox.Show(
                this,
                result.NeedsPassword
                    ? "Не удалось открыть файл подключений. Проверьте пароль и попробуйте снова."
                    : "Не удалось открыть файл подключений.",
                "Файл подключений",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            if (!result.NeedsPassword)
                return;
        }
    }

    private void CreateNewConnectionsFile(string filePath)
    {
        var dialog = new ConnectionsFilePasswordWindow(ConnectionsFilePasswordMode.CreateNew, filePath) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var result = _controller.SwitchToExternalFile(
            filePath,
            dialog.Password,
            dialog.RememberPassword,
            _mainVm.Computers.Select(item => item.ToConfig()).ToList());

        if (!result.Succeeded || result.Storage is null)
        {
            MessageBox.Show(
                this,
                "Не удалось создать внешний файл подключений.",
                "Файл подключений",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        _mainVm.ApplyConnectionsSourceChange(true, result.Storage, result.Computers);
        RefreshConnectionsSourceUi();
    }

    private void RefreshConnectionsSourceUi()
    {
        var settings = _settingsService.Load();
        if (string.IsNullOrWhiteSpace(settings.ConnectionsFilePath))
        {
            TxtConnectionsSource.Text = @"Локальный файл: %AppData%\ScreensView\computers.json";
            BtnUseLocal.IsEnabled = false;
            return;
        }

        TxtConnectionsSource.Text = settings.ConnectionsFilePath;
        BtnUseLocal.IsEnabled = true;
    }
}
