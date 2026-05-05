using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScreensView.Viewer.Helpers;
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
    private ComputerViewModel? _selectionAnchor;

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
        if (((Border)sender).DataContext is not ComputerViewModel vm)
            return;

        if (e.ClickCount == 2)
        {
            OpenZoomWindow(vm);
            e.Handled = true;
            return;
        }

        var modifiers = Keyboard.Modifiers;
        _selectionAnchor = ComputerSelectionHelper.ApplyClick(
            _vm.Computers,
            vm,
            _selectionAnchor,
            modifiers.HasFlag(ModifierKeys.Control),
            modifiers.HasFlag(ModifierKeys.Shift));
        e.Handled = true;
    }

    private void Card_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (((Border)sender).DataContext is not ComputerViewModel vm)
            return;

        _selectionAnchor = ComputerSelectionHelper.ApplyRightClick(_vm.Computers, vm, _selectionAnchor);
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

    private List<ComputerViewModel> GetMenuTargets(object sender)
    {
        var vm = GetMenuVm(sender);
        if (vm == null)
            return [];

        return ComputerSelectionHelper.GetContextMenuTargets(_vm.Computers, vm).ToList();
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
        var targets = GetMenuTargets(sender);
        if (targets.Count == 0) return;

        try
        {
            await _vm.RunLlmNowForComputersAsync(targets);
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
        var targets = GetMenuTargets(sender);
        if (targets.Count == 0) return;

        var reachable = new List<string>();
        var unreachable = new List<string>();
        var errors = new List<string>();

        foreach (var vm in targets)
        {
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(vm.Host, 3000);
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                    reachable.Add(vm.Name);
                else
                    unreachable.Add(vm.Name);
            }
            catch (Exception ex)
            {
                errors.Add($"{vm.Name}: {ex.Message}");
            }
        }

        var message = FormatPingSummary(reachable, unreachable, errors);
        var icon = unreachable.Count == 0 && errors.Count == 0
            ? MessageBoxImage.Information
            : MessageBoxImage.Warning;
        MessageBox.Show(message, LocalizationService.Get("Str.Msg.Ping"), MessageBoxButton.OK, icon);
    }

    private void TileMenu_Rdp(object sender, RoutedEventArgs e)
    {
        LaunchForTargets(sender, LocalizationService.Get("Str.Menu.Rdp"),
            vm => System.Diagnostics.Process.Start("mstsc.exe", $"/v:{vm.Host}"));
    }

    private void TileMenu_DameWare(object sender, RoutedEventArgs e)
    {
        LaunchForTargets(sender, LocalizationService.Get("Str.Menu.DameWare"), vm =>
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = @"C:\Program Files (x86)\SolarWinds\Dameware Remote Support\DWRCC.exe",
                Arguments = $"-c: -h: -m:{vm.Host} -a:1"
            };
            System.Diagnostics.Process.Start(psi);
        });
    }

    private void TileMenu_OpenShare(object sender, RoutedEventArgs e)
    {
        LaunchForTargets(sender, LocalizationService.Get("Str.Menu.OpenShare"),
            vm => System.Diagnostics.Process.Start("explorer.exe", $@"\\{vm.Host}\c$"));
    }

    private async void TileMenu_Restart(object sender, RoutedEventArgs e)
    {
        var targets = GetMenuTargets(sender);
        if (targets.Count == 0) return;

        if (MessageBox.Show(
                FormatConfirmMessage(targets, "Str.Msg.RestartConfirm", "Str.Msg.RestartConfirmN"),
                LocalizationService.Get("Str.Msg.RestartTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var dlg = new Views.CredentialsDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        await RunPowerOperationAsync(
            targets,
            LocalizationService.Get("Str.Msg.RestartTitle"),
            "Str.Msg.RestartSent",
            "Str.Msg.RestartSentN",
            (vm, username, password) => Services.RemotePowerService.RestartAsync(vm.Host, username, password),
            dlg.Username,
            dlg.Password);
    }

    private async void TileMenu_Shutdown(object sender, RoutedEventArgs e)
    {
        var targets = GetMenuTargets(sender);
        if (targets.Count == 0) return;

        if (MessageBox.Show(
                FormatConfirmMessage(targets, "Str.Msg.ShutdownConfirm", "Str.Msg.ShutdownConfirmN"),
                LocalizationService.Get("Str.Msg.ShutdownTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var dlg = new Views.CredentialsDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        await RunPowerOperationAsync(
            targets,
            LocalizationService.Get("Str.Msg.ShutdownTitle"),
            "Str.Msg.ShutdownSent",
            "Str.Msg.ShutdownSentN",
            (vm, username, password) => Services.RemotePowerService.ShutdownAsync(vm.Host, username, password),
            dlg.Username,
            dlg.Password);
    }

    private void TileMenu_Delete(object sender, RoutedEventArgs e)
    {
        var targets = GetMenuTargets(sender);
        if (targets.Count == 0) return;

        if (MessageBox.Show(
                FormatConfirmMessage(targets, "Str.Msg.DeleteConfirm", "Str.Msg.DeleteConfirmN"),
                LocalizationService.Get("Str.Msg.DeleteTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _vm.RemoveComputers(targets);
        if (_selectionAnchor != null && !_vm.Computers.Contains(_selectionAnchor))
            _selectionAnchor = null;
    }

    private static string FormatConfirmMessage(
        IReadOnlyList<ComputerViewModel> targets,
        string singleResourceKey,
        string multipleResourceKey)
    {
        return targets.Count == 1
            ? string.Format(LocalizationService.Get(singleResourceKey), targets[0].Name)
            : string.Format(LocalizationService.Get(multipleResourceKey),
                ComputerListHelpers.FormatNames(targets.Select(vm => vm.Name)));
    }

    private static string FormatPingSummary(
        IReadOnlyList<string> reachable,
        IReadOnlyList<string> unreachable,
        IReadOnlyList<string> errors)
    {
        var parts = new List<string>();
        if (reachable.Count > 0)
            parts.Add(string.Format(LocalizationService.Get("Str.Msg.PingReachable"),
                ComputerListHelpers.FormatNames(reachable)));
        if (unreachable.Count > 0)
            parts.Add(string.Format(LocalizationService.Get("Str.Msg.PingUnreachable"),
                ComputerListHelpers.FormatNames(unreachable)));
        if (errors.Count > 0)
            parts.Add(string.Format(LocalizationService.Get("Str.Msg.OperationErrors"),
                string.Join(Environment.NewLine, errors)));
        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private void LaunchForTargets(
        object sender,
        string title,
        Action<ComputerViewModel> launch)
    {
        var targets = GetMenuTargets(sender);
        if (targets.Count == 0) return;

        var errors = new List<string>();
        foreach (var vm in targets)
        {
            try
            {
                launch(vm);
            }
            catch (Exception ex)
            {
                errors.Add($"{vm.Name}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            MessageBox.Show(
                string.Format(LocalizationService.Get("Str.Msg.OperationErrors"), string.Join(Environment.NewLine, errors)),
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task RunPowerOperationAsync(
        IReadOnlyList<ComputerViewModel> targets,
        string title,
        string singleSuccessResourceKey,
        string multipleSuccessResourceKey,
        Func<ComputerViewModel, string?, string?, Task> operation,
        string? username,
        string? password)
    {
        var succeeded = new List<string>();
        var errors = new List<string>();

        foreach (var vm in targets)
        {
            try
            {
                await operation(vm, username, password);
                succeeded.Add(vm.Name);
            }
            catch (Exception ex)
            {
                errors.Add($"{vm.Name}: {ex.Message}");
            }
        }

        var parts = new List<string>();
        if (succeeded.Count == 1)
            parts.Add(string.Format(LocalizationService.Get(singleSuccessResourceKey), succeeded[0]));
        else if (succeeded.Count > 1)
            parts.Add(string.Format(LocalizationService.Get(multipleSuccessResourceKey),
                ComputerListHelpers.FormatNames(succeeded)));
        if (errors.Count > 0)
            parts.Add(string.Format(LocalizationService.Get("Str.Msg.OperationErrors"),
                string.Join(Environment.NewLine, errors)));

        MessageBox.Show(
            string.Join(Environment.NewLine + Environment.NewLine, parts),
            title,
            MessageBoxButton.OK,
            errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
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
