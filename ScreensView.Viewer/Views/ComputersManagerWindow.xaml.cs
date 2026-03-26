using System.Windows;
using System.Windows.Controls;

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

    private void ComputersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = Selected != null;
        BtnEdit.IsEnabled = hasSelection;
        BtnDelete.IsEnabled = hasSelection;
        BtnInstall.IsEnabled = hasSelection;
        BtnUninstall.IsEnabled = hasSelection;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var win = new AddEditComputerWindow(null) { Owner = this };
        if (win.ShowDialog() == true && win.Result != null)
            _mainVm.AddComputer(win.Result);
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
        if (Selected == null) return;
        if (MessageBox.Show($"Удалить компьютер '{Selected.Name}'?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _mainVm.RemoveComputer(Selected);
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        if (Selected == null) return;
        var creds = new CredentialsDialog { Owner = this };
        if (creds.ShowDialog() != true) return;
        new InstallProgressWindow(InstallProgressWindow.Mode.Install, [Selected.ToConfig()], creds.Username, creds.Password) { Owner = this }.ShowDialog();
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (Selected == null) return;
        if (MessageBox.Show($"Удалить агент с '{Selected.Name}'?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var creds = new CredentialsDialog { Owner = this };
        if (creds.ShowDialog() != true) return;
        new InstallProgressWindow(InstallProgressWindow.Mode.Uninstall, [Selected.ToConfig()], creds.Username, creds.Password) { Owner = this }.ShowDialog();
    }
}
