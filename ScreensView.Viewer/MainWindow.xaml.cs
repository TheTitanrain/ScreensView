using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScreensView.Viewer.Services;
using ScreensView.Viewer.ViewModels;
using ScreensView.Viewer.Views;

namespace ScreensView.Viewer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ConnectionsStorageController _controller;
    private readonly IViewerSettingsService _settingsService;

    private const double MinTileWidth = 240.0;
    private const double TileMargin = 12.0;
    private const double TileInfoBarHeight = 28.0;
    private const double TileBorderAspect = 9.0 / 16.0;

    private WrapPanel? _tileWrapPanel;

    internal MainWindow(MainViewModel vm, ConnectionsStorageController controller, IViewerSettingsService settingsService)
    {
        InitializeComponent();
        _vm = vm;
        _controller = controller;
        _settingsService = settingsService;
        DataContext = _vm;
    }

    private void ManageComputers_Click(object sender, RoutedEventArgs e)
    {
        var win = new ComputersManagerWindow(_vm, _controller, _settingsService);
        win.Owner = this;
        win.ShowDialog();
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (((Border)sender).DataContext is ComputerViewModel vm)
            OpenZoomWindow(vm);
        e.Handled = true;
    }

    private void OpenZoomWindow(ComputerViewModel vm)
    {
        if (vm.Status != ComputerStatus.Locked)
            new ScreenshotZoomWindow(vm) { Owner = this }.Show();
    }

    private static ComputerViewModel? GetMenuVm(object sender)
    {
        if (sender is MenuItem mi
            && mi.Parent is ContextMenu cm
            && cm.PlacementTarget is FrameworkElement fe
            && fe.DataContext is ComputerViewModel vm)
            return vm;
        return null;
    }

    private void TileMenu_Open(object sender, RoutedEventArgs e)
    {
        var vm = GetMenuVm(sender);
        if (vm != null)
            OpenZoomWindow(vm);
    }

    private void TileMenu_Edit(object sender, RoutedEventArgs e)
    {
        var vm = GetMenuVm(sender);
        if (vm == null) return;
        var win = new Views.AddEditComputerWindow(vm.ToConfig()) { Owner = this };
        if (win.ShowDialog() == true && win.Result != null)
            _vm.UpdateComputer(vm, win.Result);
    }

    private async void TileMenu_Ping(object sender, RoutedEventArgs e)
    {
        var vm = GetMenuVm(sender);
        if (vm == null) return;
        try
        {
            using var http = new Services.AgentHttpClient((_, thumbprint) =>
            {
                vm.CertThumbprint = thumbprint;
                _vm.SaveComputers();
            });
            bool ok = await http.CheckHealthAsync(vm.ToConfig());
            MessageBox.Show(
                ok ? $"{vm.Name}: агент доступен." : $"{vm.Name}: агент недоступен.",
                "Пинг",
                MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка: {ex.Message}", "Пинг", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TileMenu_Delete(object sender, RoutedEventArgs e)
    {
        var vm = GetMenuVm(sender);
        if (vm == null) return;
        if (MessageBox.Show($"Удалить «{vm.Name}»?", "Удаление",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _vm.RemoveComputer(vm);
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _vm.Dispose();
    }

    private void WrapPanel_Loaded(object sender, RoutedEventArgs e)
    {
        _tileWrapPanel = (WrapPanel)sender;
        UpdateTileSize();
    }

    private void ScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTileSize();
    }

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Fires when vertical scrollbar appears/disappears (e.g. computers collection changes),
        // reducing ViewportWidth without a SizeChanged event.
        if (e.ViewportWidthChange != 0)
            UpdateTileSize();
    }

    private void UpdateTileSize()
    {
        if (_tileWrapPanel == null) return;
        double availableWidth = _screensScrollViewer?.ViewportWidth ?? 0;
        if (availableWidth <= 0) return;
        int columns = Math.Max(1, (int)(availableWidth / MinTileWidth));
        double tileWidth = availableWidth / columns;
        double borderWidth = tileWidth - TileMargin;
        _tileWrapPanel.ItemWidth = tileWidth;
        _tileWrapPanel.ItemHeight = borderWidth * TileBorderAspect + TileMargin + TileInfoBarHeight;
    }
}
