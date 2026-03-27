using ScreensView.Viewer.Helpers;

namespace ScreensView.Tests;

public class ComputerListHelpersTests
{
    [Fact]
    public void FormatNames_UpTo10_ReturnsAllJoined()
    {
        var names = new[] { "A", "B", "C", "D", "E" };
        Assert.Equal("A, B, C, D, E", ComputerListHelpers.FormatNames(names));
    }

    [Fact]
    public void FormatNames_Over10_TruncatesWithSuffix()
    {
        var names = Enumerable.Range(1, 12).Select(i => $"N{i}");
        Assert.Equal("N1, N2, N3, N4, N5, N6, N7, N8, N9, N10 и ещё 2",
            ComputerListHelpers.FormatNames(names));
    }
}
