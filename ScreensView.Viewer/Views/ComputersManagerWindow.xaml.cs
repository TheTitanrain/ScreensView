using System.Windows;
using System.Windows.Controls;
using ScreensView.Viewer.Helpers;
using ScreensView.Viewer.Services;

namespace ScreensView.Viewer.Views;

public partial class ComputersManagerWindow : Window
{
    private readonly ViewModels.MainViewModel _mainVm;
    private readonly ConnectionsSourceWorkflowService _workflow;

    internal ComputersManagerWindow(
        ViewModels.MainViewModel mainVm,
        ConnectionsSourceWorkflowService workflow)
    {
        InitializeComponent();
        _mainVm = mainVm;
        _workflow = workflow;
        ComputersList.ItemsSource = mainVm.Computers;
        RefreshConnectionsSourceUi();
        Loaded += OnLoaded;
    }

    private ViewModels.ComputerViewModel? Selected => ComputersList.SelectedItem as ViewModels.ComputerViewModel;

    private List<Shared.Models.ComputerConfig> SelectedConfigs =>
        ComputersList.SelectedItems.Cast<ViewModels.ComputerViewModel>()
            .Select(vm => vm.ToConfig()).ToList();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        UpdateLayout();
        var panelWidth = Math.Max(ToolbarPanel.ActualWidth, ConnectionsStatusPanel.ActualWidth);
        var computed = WindowWidthHelper.ComputeMinWidth(
            panelWidth,
            Content is FrameworkElement content ? content.ActualWidth : ActualWidth,
            ActualWidth,
            SystemParameters.WorkArea.Width);
        Width = computed;
        MinWidth = computed;
    }

    private void ComputersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var count = ComputersList.SelectedItems.Count;
        BtnEdit.IsEnabled               = count == 1;
        BtnDelete.IsEnabled             = count >= 1;
        BtnInstall.IsEnabled            = count >= 1;
        BtnUninstall.IsEnabled          = count >= 1;
        BtnInstallDotNetRuntimes.IsEnabled = count >= 1;
    }

    private void EnabledCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.DataContext is not ViewModels.ComputerViewModel vm)
            return;

        _mainVm.SetComputerEnabled(vm, checkBox.IsChecked == true);
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

    private void InstallDotNetRuntimes_Click(object sender, RoutedEventArgs e)
    {
        var configs = SelectedConfigs;
        if (configs.Count == 0) return;
        LaunchOperation(InstallProgressWindow.Mode.InstallDotNetRuntimes, configs);
    }

    private void LaunchInstall(List<Shared.Models.ComputerConfig> configs) =>
        LaunchOperation(InstallProgressWindow.Mode.Install, configs);

    private void LaunchOperation(InstallProgressWindow.Mode mode, List<Shared.Models.ComputerConfig> configs)
    {
        var creds = new CredentialsDialog { Owner = this };
        if (creds.ShowDialog() != true) return;
        new InstallProgressWindow(mode, configs, creds.Username, creds.Password) { Owner = this }.ShowDialog();
    }

    private void UpdateAllAgents_Click(object sender, RoutedEventArgs e)
    {
        var computers = _mainVm.Computers.Where(c => c.IsEnabled).Select(c => c.ToConfig()).ToList();
        if (computers.Count == 0)
        {
            MessageBox.Show(
                this,
                "Нет активных компьютеров для обновления.",
                "Обновление агентов",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var creds = new CredentialsDialog { Owner = this };
        if (creds.ShowDialog() != true) return;

        new InstallProgressWindow(InstallProgressWindow.Mode.UpdateAll, computers, creds.Username, creds.Password)
        {
            Owner = this
        }.ShowDialog();
    }

    private void RefreshConnectionsSourceUi()
    {
        var state = _workflow.GetCurrentUiState();
        TxtConnectionsSource.Text = state.DisplayText;
        TxtConnectionsHint.Text = state.HintText;
    }
}
