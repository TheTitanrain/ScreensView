using System.Windows;
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

    private void UpdateAllAgents_Click(object sender, RoutedEventArgs e)
    {
        var computers = _vm.Computers.Where(c => c.IsEnabled).Select(c => c.ToConfig()).ToList();
        if (computers.Count == 0)
        {
            MessageBox.Show("Нет активных компьютеров для обновления.", "Обновление агентов",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var creds = new CredentialsDialog { Owner = this };
        if (creds.ShowDialog() != true) return;
        var win = new InstallProgressWindow(InstallProgressWindow.Mode.UpdateAll, computers, creds.Username, creds.Password);
        win.Owner = this;
        win.ShowDialog();
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (((System.Windows.Controls.Border)sender).DataContext is ComputerViewModel vm
            && vm.Status != ComputerStatus.Locked)
            new ScreenshotZoomWindow(vm) { Owner = this }.Show();
        e.Handled = true;
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _vm.Dispose();
    }
}
