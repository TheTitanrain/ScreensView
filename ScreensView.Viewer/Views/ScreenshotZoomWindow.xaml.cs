using System.Windows;
using System.Windows.Input;
using ScreensView.Viewer.ViewModels;

namespace ScreensView.Viewer.Views;

public partial class ScreenshotZoomWindow : Window
{
    public ScreenshotZoomWindow(ComputerViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
        base.OnKeyDown(e);
    }
}
