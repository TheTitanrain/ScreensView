using System.Windows;
using ScreensView.Viewer.ViewModels;

namespace ScreensView.Viewer.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
