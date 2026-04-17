using System.Windows;
using ScreensView.Viewer.Services;
using ScreensView.Viewer.ViewModels;
using ScreensView.Viewer.Views;

namespace ScreensView.Viewer;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        using var startupShutdown = new StartupShutdownScope(new ApplicationShutdownModeHost(this));
        await ViewerUpdateService.CheckAndUpdateAsync();

        var settingsService = new ViewerSettingsService();
        LocalizationService.Apply(settingsService.Load().Language ?? "auto");
        var logService = new ViewerLogService();
        var controller = new ConnectionsStorageController(
            settingsService,
            () => new ComputerStorageService(),
            (filePath, password) => new EncryptedComputerStorageService(filePath, password));
        var workflow = new ConnectionsSourceWorkflowService(
            controller,
            settingsService,
            new ConnectionsSourceDialogs());

        var startupOptions = ViewerStartupOptionsParser.Parse(Environment.GetCommandLineArgs());
        var startup = workflow.ResolveStartup(startupOptions);
        if (startup is null)
        {
            Shutdown();
            return;
        }

        MainWindow? mainWindow = null;
        MainViewModel? viewModel = null;
        AgentHttpClient? http = null;
        http = new AgentHttpClient((computer, thumbprint) =>
        {
            var vm = viewModel?.Computers.FirstOrDefault(item => item.Id == computer.Id);
            if (vm is null || viewModel is null)
                return;

            vm.CertThumbprint = thumbprint;
            viewModel.SaveComputers();
            // Invalidate cached HttpClient so the next request uses a handler
            // that validates against the newly pinned thumbprint.
            http!.InvalidateClient(computer.Id);
        });

        var poller = new ScreenshotPollerService(http, new WpfUiDispatcher());
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
            log: logService);

        mainWindow = new MainWindow(viewModel, workflow);
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}

internal interface IShutdownModeHost
{
    ShutdownMode ShutdownMode { get; set; }
    bool IsShuttingDown { get; }
}

internal sealed class ApplicationShutdownModeHost(Application application) : IShutdownModeHost
{
    private readonly Application _application = application ?? throw new ArgumentNullException(nameof(application));

    public ShutdownMode ShutdownMode
    {
        get => _application.ShutdownMode;
        set => _application.ShutdownMode = value;
    }

    public bool IsShuttingDown
        => _application.Dispatcher.HasShutdownStarted || _application.Dispatcher.HasShutdownFinished;
}

internal sealed class StartupShutdownScope : IDisposable
{
    private readonly IShutdownModeHost _host;
    private readonly ShutdownMode _previousMode;
    private bool _disposed;

    public StartupShutdownScope(IShutdownModeHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _previousMode = host.ShutdownMode;
        _host.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_host.IsShuttingDown)
            return;

        _host.ShutdownMode = _previousMode;
    }
}
