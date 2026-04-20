using System.ComponentModel;
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
    private readonly ConnectionsSourceWorkflowService _workflow;
    private bool _realClose;

    private const double MinTileWidth = 240.0;
    private const double TileMargin = 12.0;
    private const double TileInfoBarHeight = 28.0;
    private const double TileBorderAspect = 9.0 / 16.0;

    private WrapPanel? _tileWrapPanel;

    internal MainWindow(MainViewModel vm, ConnectionsSourceWorkflowService workflow)
    {
        InitializeComponent();
        _vm = vm;
        _workflow = workflow;
        DataContext = _vm;
    }

    private void ManageComputers_Click(object sender, RoutedEventArgs e)
    {
        var win = new ComputersManagerWindow(_vm, _workflow);
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

    private async void TileMenu_RunLlmNow(object sender, RoutedEventArgs e)
    {
        var vm = GetMenuVm(sender);
        if (vm == null) return;

        try
        {
            await _vm.RunLlmNowForComputerAsync(vm);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(LocalizationService.Get("Str.Msg.Error"), ex.Message), "LLM", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TileMenu_Ping(object sender, RoutedEventArgs e)
    {
        var vm = GetMenuVm(sender);
        if (vm == null) return;
        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = await ping.SendPingAsync(vm.Host, 3000);
            bool ok = reply.Status == System.Net.NetworkInformation.IPStatus.Success;
            MessageBox.Show(
                ok ? string.Format(LocalizationService.Get("Str.Msg.PingOk"), vm.Name)
                   : string.Format(LocalizationService.Get("Str.Msg.PingFail"), vm.Name),
                LocalizationService.Get("Str.Msg.Ping"),
                MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(LocalizationService.Get("Str.Msg.Error"), ex.Message), LocalizationService.Get("Str.Msg.Ping"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TileMenu_Rdp(object sender, RoutedEventArgs e)
    {
        var vm = GetMenuVm(sender);
        if (vm == null) return;
        System.Diagnostics.Process.Start("mstsc.exe", $"/v:{vm.Host}");
    }

    private void TileMenu_DameWare(object sender, RoutedEventArgs e)
    {
        var vm = GetMenuVm(sender);
        if (vm == null) return;
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = @"C:\Program Files (x86)\SolarWinds\Dameware Remote Support\DWRCC.exe",
            Arguments = $"-c: -h: -m:{vm.Host} -a:1"
        };
        System.Diagnostics.Process.Start(psi);
    }

    private void TileMenu_OpenShare(object sender, RoutedEventArgs e)
    {
        var vm = GetMenuVm(sender);
        if (vm == null) return;
        System.Diagnostics.Process.Start("explorer.exe", $@"\\{vm.Host}\c$");
    }

    private async void TileMenu_Restart(object sender, RoutedEventArgs e)
    {
        var vm = GetMenuVm(sender);
        if (vm == null) return;
        if (MessageBox.Show(
                string.Format(LocalizationService.Get("Str.Msg.RestartConfirm"), vm.Name),
                LocalizationService.Get("Str.Msg.RestartTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var dlg = new Views.CredentialsDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;
        try
        {
            await Services.RemotePowerService.RestartAsync(vm.Host, dlg.Username, dlg.Password);
            MessageBox.Show(
                string.Format(LocalizationService.Get("Str.Msg.RestartSent"), vm.Name),
                LocalizationService.Get("Str.Msg.RestartTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(LocalizationService.Get("Str.Msg.Error"), ex.Message), LocalizationService.Get("Str.Msg.RestartTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TileMenu_Shutdown(object sender, RoutedEventArgs e)
    {
        var vm = GetMenuVm(sender);
        if (vm == null) return;
        if (MessageBox.Show(
                string.Format(LocalizationService.Get("Str.Msg.ShutdownConfirm"), vm.Name),
                LocalizationService.Get("Str.Msg.ShutdownTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var dlg = new Views.CredentialsDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;
        try
        {
            await Services.RemotePowerService.ShutdownAsync(vm.Host, dlg.Username, dlg.Password);
            MessageBox.Show(
                string.Format(LocalizationService.Get("Str.Msg.ShutdownSent"), vm.Name),
                LocalizationService.Get("Str.Msg.ShutdownTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(LocalizationService.Get("Str.Msg.Error"), ex.Message), LocalizationService.Get("Str.Msg.ShutdownTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TileMenu_Delete(object sender, RoutedEventArgs e)
    {
        var vm = GetMenuVm(sender);
        if (vm == null) return;
        if (MessageBox.Show(
                string.Format(LocalizationService.Get("Str.Msg.DeleteConfirm"), vm.Name),
                LocalizationService.Get("Str.Msg.DeleteTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _vm.RemoveComputer(vm);
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var win = new Views.SettingsWindow(_vm, _workflow) { Owner = this };
        win.ShowDialog();
    }

    internal void RequestRealClose() => _realClose = true;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_realClose && _vm.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            return;
            // Intentionally NOT calling base.OnClosing(e):
            // base raises the Closing event, which invokes Window_Closing
            // and calls _vm.Dispose() — must not happen during hide-to-tray.
        }
        base.OnClosing(e); // real close: raises Closing → Window_Closing → _vm.Dispose()
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
