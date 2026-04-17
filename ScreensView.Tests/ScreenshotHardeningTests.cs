using ScreensView.Shared;

namespace ScreensView.Tests;

public class ScreenshotHardeningTests
{
    [Theory]
    [InlineData("75", 75)]
    [InlineData("200", 100)]
    [InlineData("-5", 0)]
    [InlineData("bad", 75)]
    public void ScreenshotQuality_ParseOrDefault_ClampsIntoJpegRange(string raw, int expected)
    {
        Assert.Equal(expected, ScreenshotQuality.ParseOrDefault(raw));
    }

    [Fact]
    public void SingleFlightGate_RejectsSecondConcurrentEntryUntilLeaseDisposed()
    {
        var gate = new SingleFlightGate();
        using var first = gate.TryEnter();

        Assert.NotNull(first);
        Assert.Null(gate.TryEnter());
    }
}
