using System.Windows;
using ScreensView.Viewer.Services;
using ScreensView.Viewer.ViewModels;

namespace ScreensView.Viewer.Views;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ConnectionsSourceWorkflowService _workflow;

    internal SettingsWindow(MainViewModel vm, ConnectionsSourceWorkflowService workflow)
    {
        InitializeComponent();
        _vm = vm;
        _workflow = workflow;
        DataContext = _vm;
        RefreshConnectionsSourceUi();
    }

    private void ConnectionsFile_Click(object sender, RoutedEventArgs e)
    {
        var result = _workflow.SelectConnectionsFile(_vm.Computers.Select(item => item.ToConfig()).ToList());
        if (result.Applied && result.Storage is not null)
            _vm.ApplyConnectionsSourceChange(true, result.Storage, result.Computers);

        RefreshConnectionsSourceUi();
    }

    private void UseLocal_Click(object sender, RoutedEventArgs e)
    {
        var result = _workflow.SwitchToLocalStorage(_vm.Computers.Select(item => item.ToConfig()).ToList());
        if (result.Applied && result.Storage is not null)
            _vm.ApplyConnectionsSourceChange(true, result.Storage, result.Computers);

        RefreshConnectionsSourceUi();
    }

    private void RefreshConnectionsSourceUi()
    {
        var state = _workflow.GetCurrentUiState();
        TxtConnectionsSource.Text = state.DisplayText;
        TxtConnectionsHint.Text = state.HintText;
        BtnUseLocal.IsEnabled = state.CanSwitchToLocal;
    }
}
