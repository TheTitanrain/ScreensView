using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ScreensView.Viewer.Services;
using ScreensView.Viewer.ViewModels;
using ScreensView.Viewer.Views;

namespace ScreensView.Viewer;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        await ViewerUpdateService.CheckAndUpdateAsync();

        var settingsService = new ViewerSettingsService();
        var controller = new ConnectionsStorageController(
            settingsService,
            () => new ComputerStorageService(),
            (filePath, password) => new EncryptedComputerStorageService(filePath, password));

        var startup = ResolveStartupSource(controller, settingsService);
        if (startup is null)
        {
            Shutdown();
            return;
        }

        MainWindow? mainWindow = null;
        MainViewModel? viewModel = null;
        var http = new AgentHttpClient((computer, thumbprint) =>
        {
            var vm = viewModel?.Computers.FirstOrDefault(item => item.Id == computer.Id);
            if (vm is null || viewModel is null)
                return;

            vm.CertThumbprint = thumbprint;
            viewModel.SaveComputers();
        });

        var poller = new ScreenshotPollerService(http);
        viewModel = new MainViewModel(
            startup.Storage!,
            poller,
            settingsService,
            new AutostartService(),
            message =>
            {
                if (mainWindow is null)
                    MessageBox.Show(message, "Автозапуск", MessageBoxButton.OK, MessageBoxImage.Error);
                else
                    MessageBox.Show(mainWindow, message, "Автозапуск", MessageBoxButton.OK, MessageBoxImage.Error);
            });

        mainWindow = new MainWindow(viewModel, controller, settingsService);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private ResolveConnectionsSourceResult? ResolveStartupSource(
        ConnectionsStorageController controller,
        IViewerSettingsService settingsService)
    {
        var startup = controller.ResolveStartup();
        while (startup.NeedsPassword)
        {
            var settings = settingsService.Load();
            var dialog = new ConnectionsFilePasswordWindow(
                ConnectionsFilePasswordMode.OpenExisting,
                settings.ConnectionsFilePath);

            var accepted = dialog.ShowDialog() == true;
            if (accepted)
            {
                var result = controller.OpenExternalFile(settings.ConnectionsFilePath, dialog.Password, dialog.RememberPassword);
                if (result.Succeeded && result.Storage is not null)
                {
                    return new ResolveConnectionsSourceResult(
                        result.Storage,
                        result.Computers,
                        usesExternalFile: true,
                        needsPassword: false);
                }

                MessageBox.Show(
                    "Не удалось открыть внешний файл подключений. Проверьте пароль и повторите попытку.",
                    "Файл подключений",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                continue;
            }

            var switchToLocal = MessageBox.Show(
                "Внешний файл подключений не открыт. Переключиться на локальный файл подключений?",
                "Файл подключений",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (switchToLocal == MessageBoxResult.Yes)
            {
                var fallbackSettings = settingsService.Load();
                fallbackSettings.ConnectionsFilePath = string.Empty;
                fallbackSettings.ConnectionsFilePasswordEncrypted = string.Empty;
                settingsService.Save(fallbackSettings);
                return controller.ResolveStartup();
            }

            if (switchToLocal == MessageBoxResult.No)
                continue;

            return null;
        }

        return startup;
    }
}

public class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value != null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
