using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ScreensView.Viewer.Services;

namespace ScreensView.Viewer;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        await ViewerUpdateService.CheckAndUpdateAsync();
    }
}

public class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value != null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
