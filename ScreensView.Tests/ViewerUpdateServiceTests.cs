using ScreensView.Viewer.Services;

namespace ScreensView.Tests;

public class ViewerUpdateServiceTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("v0.1.0", 0, 1, 0)]
    [InlineData("v10.0.0", 10, 0, 0)]
    [InlineData("1.2.3", 1, 2, 3)]       // no v prefix
    [InlineData("v1.2.3.4", 1, 2, 3, 4)] // four-part version
    public void ParseVersion_ValidTag_ReturnsCorrectVersion(string tag, int major, int minor, int build, int revision = -1)
    {
        var result = ViewerUpdateService.ParseVersion(tag);

        Assert.Equal(major, result.Major);
        Assert.Equal(minor, result.Minor);
        Assert.Equal(build, result.Build);
        if (revision >= 0)
            Assert.Equal(revision, result.Revision);
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("latest")]
    public void ParseVersion_InvalidTag_ReturnsFallback(string tag)
    {
        var result = ViewerUpdateService.ParseVersion(tag);

        Assert.Equal(new Version(0, 0), result);
    }

    [Fact]
    public void ParseVersion_WithVPrefix_EqualsWithout()
    {
        var withV = ViewerUpdateService.ParseVersion("v2.3.1");
        var without = ViewerUpdateService.ParseVersion("2.3.1");

        Assert.Equal(withV, without);
    }

    [Fact]
    public void ParseVersion_NewerTag_IsGreaterThanOlder()
    {
        var older = ViewerUpdateService.ParseVersion("v1.0.0");
        var newer = ViewerUpdateService.ParseVersion("v2.0.0");

        Assert.True(newer > older);
    }
}
