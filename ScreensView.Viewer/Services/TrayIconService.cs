using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;

namespace ScreensView.Viewer.Services;

internal sealed class TrayIconService : IDisposable
{
    private readonly MainWindow _mainWindow;
    private readonly Action _onOpenSettings;
    private readonly TaskbarIcon _taskbarIcon;
    private readonly MenuItem _showHideItem;
    private readonly MenuItem _openSettingsItem;
    private readonly MenuItem _exitItem;

    private readonly DependencyPropertyChangedEventHandler _onIsVisibleChanged;
    private readonly Action _onLanguageChanged;

    internal TrayIconService(MainWindow mainWindow, Action onOpenSettings)
    {
        _mainWindow = mainWindow;
        _onOpenSettings = onOpenSettings;

        _showHideItem = new MenuItem();
        _showHideItem.Click += (_, _) =>
        {
            if (_mainWindow.IsVisible)
                _mainWindow.Hide();
            else
            {
                _mainWindow.Show();
                _mainWindow.Activate();
            }
        };

        _openSettingsItem = new MenuItem();
        _openSettingsItem.Click += (_, _) => _onOpenSettings();

        _exitItem = new MenuItem();
        _exitItem.Click += (_, _) =>
        {
            _mainWindow.RequestRealClose();
            Application.Current.Shutdown();
        };

        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(_showHideItem);
        contextMenu.Items.Add(_openSettingsItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(_exitItem);

        _taskbarIcon = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri("pack://application:,,,/screensview.ico")),
            ToolTipText = "ScreensView",
            ContextMenu = contextMenu
        };
        _taskbarIcon.ForceCreate();

        _onIsVisibleChanged = (_, _e) => RefreshMenuLabels();
        _mainWindow.IsVisibleChanged += _onIsVisibleChanged;

        _onLanguageChanged = RefreshMenuLabels;
        LocalizationService.LanguageChanged += _onLanguageChanged;

        _taskbarIcon.TrayMouseDoubleClick += (_, _) =>
        {
            _mainWindow.Show();
            _mainWindow.Activate();
        };

        RefreshMenuLabels();
    }

    private void RefreshMenuLabels()
    {
        _showHideItem.Header = _mainWindow.IsVisible
            ? LocalizationService.Get("Str.Tray.Hide")
            : LocalizationService.Get("Str.Tray.Show");
        _openSettingsItem.Header = LocalizationService.Get("Str.Tray.OpenSettings");
        _exitItem.Header = LocalizationService.Get("Str.Tray.Exit");
    }

    public void Dispose()
    {
        _mainWindow.IsVisibleChanged -= _onIsVisibleChanged;
        LocalizationService.LanguageChanged -= _onLanguageChanged;
        _taskbarIcon.Dispose();
    }
}
