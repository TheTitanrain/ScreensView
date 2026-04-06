using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ScreensView.Viewer;
using ScreensView.Viewer.ViewModels;

namespace ScreensView.Tests;

public class AppConvertersTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(LlmTileStatus.Inactive, null)]
    [InlineData(LlmTileStatus.NoDescription, "?")]
    [InlineData(LlmTileStatus.Waiting, "LLM")]
    [InlineData(LlmTileStatus.Checking, "···")]
    [InlineData(LlmTileStatus.Match, "✓")]
    [InlineData(LlmTileStatus.Mismatch, "✗")]
    [InlineData(LlmTileStatus.Error, "!")]
    public void LlmStatusToTextConverter_MapsStatuses(LlmTileStatus status, string? expected)
    {
        var converter = new LlmStatusToTextConverter();

        var result = converter.Convert(status, typeof(string), null!, Culture);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(LlmTileStatus.Inactive, 0x80, 0x88, 0x88, 0x88)]
    [InlineData(LlmTileStatus.NoDescription, 0x80, 0x88, 0x88, 0x88)]
    [InlineData(LlmTileStatus.Waiting, 0x80, 0x88, 0x88, 0x88)]
    [InlineData(LlmTileStatus.Checking, 0x99, 0x44, 0x99, 0xDD)]
    [InlineData(LlmTileStatus.Match, 0x99, 0x44, 0xCC, 0x44)]
    [InlineData(LlmTileStatus.Mismatch, 0x99, 0xFF, 0x88, 0x00)]
    [InlineData(LlmTileStatus.Error, 0x99, 0xCC, 0x44, 0x44)]
    public void LlmStatusToBackgroundConverter_MapsStatuses(
        LlmTileStatus status,
        byte a,
        byte r,
        byte g,
        byte b)
    {
        var converter = new LlmStatusToBackgroundConverter();

        var result = converter.Convert(status, typeof(Brush), null!, Culture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.FromArgb(a, r, g, b), brush.Color);
    }

    [Fact]
    public void LlmStatusToBackgroundConverter_WhenValueIsUnknown_ReturnsTransparentBrush()
    {
        var converter = new LlmStatusToBackgroundConverter();

        var result = converter.Convert("bad-value", typeof(Brush), null!, Culture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Colors.Transparent, brush.Color);
    }

    [Theory]
    [InlineData(LlmTileStatus.Inactive, Visibility.Collapsed)]
    [InlineData(LlmTileStatus.NoDescription, Visibility.Visible)]
    [InlineData(LlmTileStatus.Waiting, Visibility.Visible)]
    [InlineData(LlmTileStatus.Checking, Visibility.Visible)]
    [InlineData(LlmTileStatus.Match, Visibility.Visible)]
    [InlineData(LlmTileStatus.Mismatch, Visibility.Visible)]
    [InlineData(LlmTileStatus.Error, Visibility.Visible)]
    public void LlmStatusToVisibilityConverter_MapsStatuses(LlmTileStatus status, Visibility expected)
    {
        var converter = new LlmStatusToVisibilityConverter();

        var result = converter.Convert(status, typeof(Visibility), null!, Culture);

        Assert.Equal(expected, result);
    }
}
