using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ScreensView.Viewer.Models;
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
        var downloadService = new ModelDownloadService();
        var inferenceService = new LlmInferenceService(downloadService);
        var llmCheckService = new LlmCheckService(inferenceService);
        viewModel = new MainViewModel(
            startup.Storage!,
            poller,
            settingsService,
            new AutostartService(),
            (title, message) =>
            {
                if (mainWindow is null)
                    MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
                else
                    MessageBox.Show(mainWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            },
            llmCheckService,
            downloadService);

        mainWindow = new MainWindow(viewModel, controller, settingsService);
        if (!downloadService.IsModelReady)
            StartModelDownloadAsync(downloadService, viewModel);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private static async void StartModelDownloadAsync(
        ModelDownloadService downloadService,
        MainViewModel viewModel)
    {
        var progress = new Progress<double>(p => viewModel.ModelDownloadProgress = p);
        try
        {
            await downloadService.DownloadAsync(progress, viewModel.AppToken);
            viewModel.ModelDownloadProgress = -1;
        }
        catch (OperationCanceledException)
        {
            viewModel.ModelDownloadProgress = -1;
        }
        catch (Exception ex)
        {
            viewModel.ModelDownloadProgress = -1;
            viewModel.ReportDownloadError(ex.Message);
        }
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

public class LlmBorderBrushConverter : IValueConverter
{
    public static readonly LlmBorderBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is LlmCheckResult { IsError: false, IsMatch: true })
            return new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x44, 0xCC, 0x44)); // green
        if (value is LlmCheckResult { IsError: false, IsMatch: false })
            return new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xFF, 0x88, 0x00)); // orange
        return System.Windows.Media.Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class LlmBorderThicknessConverter : IValueConverter
{
    public static readonly LlmBorderThicknessConverter Instance = new();

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is LlmCheckResult { IsError: false, IsMatch: true })
            return new System.Windows.Thickness(2);
        if (value is LlmCheckResult { IsError: false, IsMatch: false })
            return new System.Windows.Thickness(3);
        return new System.Windows.Thickness(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class LlmTooltipConverter : IValueConverter
{
    public static readonly LlmTooltipConverter Instance = new();

    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isChecking && isChecking)
            return "LLM: analysing...";
        if (value is LlmCheckResult result)
        {
            var prefix = result.IsError ? "LLM: Error" :
                         result.IsMatch ? "LLM: Match" : "LLM: Mismatch";
            return $"{prefix} — {result.Explanation}";
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ModelDownloadActiveConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value is double d && d >= 0;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
