using ScreensView.Viewer.Helpers;

namespace ScreensView.Tests;

public class WindowWidthHelperTests
{
    [Fact]
    public void ComputeMinWidth_ReturnsTargetWhenBelowWorkArea()
    {
        // panelWidth=800, contentWidth=880, windowWidth=900 → frameWidth=20, target=820
        var result = WindowWidthHelper.ComputeMinWidth(800, 880, 900, 2000);
        Assert.Equal(820, result);
    }

    [Fact]
    public void ComputeMinWidth_ClampsToWorkAreaWhenTargetExceeds()
    {
        // panelWidth=1800, contentWidth=880, windowWidth=900 → frameWidth=20, target=1820 > workArea=1600
        var result = WindowWidthHelper.ComputeMinWidth(1800, 880, 900, 1600);
        Assert.Equal(1600, result);
    }

    [Fact]
    public void ComputeMinWidth_ReturnsWorkAreaWhenExactlyEqual()
    {
        // target=1600, workArea=1600 → clamped to 1600
        var result = WindowWidthHelper.ComputeMinWidth(1580, 880, 900, 1600);
        Assert.Equal(1600, result);
    }
}
