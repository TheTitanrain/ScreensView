using System.Windows;
using System.Windows.Controls;
using ScreensView.Viewer.Helpers;

namespace ScreensView.Viewer.Views;

public partial class ComputersManagerWindow : Window
{
    private readonly ViewModels.MainViewModel _mainVm;

    public ComputersManagerWindow(ViewModels.MainViewModel mainVm)
    {
        InitializeComponent();
        _mainVm = mainVm;
        ComputersList.ItemsSource = mainVm.Computers;
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
        LaunchInstall(SelectedConfigs);
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

        LaunchUninstall(configs);
    }

    private void LaunchInstall(List<Shared.Models.ComputerConfig> configs)
    {
        var creds = new CredentialsDialog { Owner = this };
        if (creds.ShowDialog() != true) return;
        new InstallProgressWindow(InstallProgressWindow.Mode.Install, configs, creds.Username, creds.Password) { Owner = this }.ShowDialog();
    }

    private void LaunchUninstall(List<Shared.Models.ComputerConfig> configs)
    {
        var creds = new CredentialsDialog { Owner = this };
        if (creds.ShowDialog() != true) return;
        new InstallProgressWindow(InstallProgressWindow.Mode.Uninstall, configs, creds.Username, creds.Password) { Owner = this }.ShowDialog();
    }
}
