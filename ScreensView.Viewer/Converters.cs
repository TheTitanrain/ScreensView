using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ScreensView.Viewer.Models;
using ScreensView.Viewer.Services;
using ScreensView.Viewer.ViewModels;

namespace ScreensView.Viewer;

public class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value != null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class LlmBorderBrushConverter : IValueConverter
{
    public static readonly LlmBorderBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is LlmCheckResult { IsError: false, IsMatch: true })
            return new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x44, 0xCC, 0x44));
        if (value is LlmCheckResult { IsError: false, IsMatch: false })
            return new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xFF, 0x88, 0x00));
        return System.Windows.Media.Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class LlmBorderThicknessConverter : IValueConverter
{
    public static readonly LlmBorderThicknessConverter Instance = new();

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is LlmCheckResult { IsError: false, IsMatch: true })
            return new Thickness(2);
        if (value is LlmCheckResult { IsError: false, IsMatch: false })
            return new Thickness(3);
        return new Thickness(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class LlmStatusToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value is LlmTileStatus status ? status switch
        {
            LlmTileStatus.NoDescription => "?",
            LlmTileStatus.Waiting => "LLM",
            LlmTileStatus.Checking => "···",
            LlmTileStatus.Match => "✓",
            LlmTileStatus.Mismatch => "✗",
            LlmTileStatus.Error => "!",
            _ => null
        } : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class LlmStatusToBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = value is LlmTileStatus status ? status switch
        {
            LlmTileStatus.Inactive or LlmTileStatus.NoDescription or LlmTileStatus.Waiting
                => System.Windows.Media.Color.FromArgb(0x80, 0x88, 0x88, 0x88),
            LlmTileStatus.Checking
                => System.Windows.Media.Color.FromArgb(0x99, 0x44, 0x99, 0xDD),
            LlmTileStatus.Match
                => System.Windows.Media.Color.FromArgb(0x99, 0x44, 0xCC, 0x44),
            LlmTileStatus.Mismatch
                => System.Windows.Media.Color.FromArgb(0x99, 0xFF, 0x88, 0x00),
            LlmTileStatus.Error
                => System.Windows.Media.Color.FromArgb(0x99, 0xCC, 0x44, 0x44),
            _ => System.Windows.Media.Colors.Transparent
        } : System.Windows.Media.Colors.Transparent;

        return new System.Windows.Media.SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class LlmStatusToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value is LlmTileStatus.Inactive ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class LlmTooltipConverter : IValueConverter
{
    public static readonly LlmTooltipConverter Instance = new();

    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isChecking && isChecking)
            return LocalizationService.Get("Str.Llm.Analysing");
        if (value is LlmCheckResult result)
        {
            var prefix = result.IsError ? LocalizationService.Get("Str.Llm.Error") :
                         result.IsMatch ? LocalizationService.Get("Str.Llm.Match")
                                        : LocalizationService.Get("Str.Llm.Mismatch");
            return $"{prefix} — {result.Explanation}";
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ModelDownloadActiveConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value is double d && d >= 0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
