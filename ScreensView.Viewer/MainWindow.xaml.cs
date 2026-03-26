using System.Windows;
using ScreensView.Viewer.Services;
using ScreensView.Viewer.ViewModels;
using ScreensView.Viewer.Views;

namespace ScreensView.Viewer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();

        var storage = new ComputerStorageService();
        var http = new AgentHttpClient((computer, thumbprint) =>
        {
            // Find the matching ComputerViewModel and persist the pinned thumbprint
            var vm = _vm?.Computers.FirstOrDefault(c => c.Id == computer.Id);
            if (vm != null)
            {
                vm.CertThumbprint = thumbprint;
                _vm!.SaveComputers();
            }
        });
        var poller = new ScreenshotPollerService(http);
        _vm = new MainViewModel(storage, poller);
        DataContext = _vm;
    }

    private void ManageComputers_Click(object sender, RoutedEventArgs e)
    {
        var win = new ComputersManagerWindow(_vm);
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

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _vm.Dispose();
    }
}
