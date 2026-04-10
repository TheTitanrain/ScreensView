using Jint;
using System.Dynamic;

namespace ScreensView.Tests;

public class WebsiteDownloadScriptTests
{
    [Fact]
    public void ResolveDownloadState_UsesDirectAssetWhenExactlyOneViewerExecutableMatches()
    {
        var result = ResolveDownloadState(new
        {
            assets = new[]
            {
                new { name = "ScreensView.Viewer-win-x64.exe", browser_download_url = "https://example.test/viewer.exe" }
            }
        });

        Assert.Equal("direct", result["source"]);
        Assert.Equal("https://example.test/viewer.exe", result["href"]);
        Assert.Equal("Download Viewer", result["label"]);
    }

    [Fact]
    public void ResolveDownloadState_FallsBackWhenApiResponseIsMissing()
    {
        var result = ResolveDownloadState(null);

        Assert.Equal("fallback", result["source"]);
        Assert.Equal(FallbackUrl, result["href"]);
        Assert.Equal("Open latest release", result["label"]);
    }

    [Fact]
    public void ResolveDownloadState_FallsBackWhenNoViewerExecutableExists()
    {
        var result = ResolveDownloadState(new
        {
            assets = new[]
            {
                new { name = "notes.txt", browser_download_url = "https://example.test/notes.txt" }
            }
        });

        Assert.Equal("fallback", result["source"]);
        Assert.Equal(FallbackUrl, result["href"]);
        Assert.Equal("Open latest release", result["label"]);
    }

    [Fact]
    public void ResolveDownloadState_FallsBackWhenMultipleViewerExecutablesExist()
    {
        var result = ResolveDownloadState(new
        {
            assets = new[]
            {
                new { name = "ScreensView.Viewer-win-x64.exe", browser_download_url = "https://example.test/viewer-x64.exe" },
                new { name = "ScreensView.Viewer-win-arm64.exe", browser_download_url = "https://example.test/viewer-arm64.exe" }
            }
        });

        Assert.Equal("fallback", result["source"]);
        Assert.Equal(FallbackUrl, result["href"]);
        Assert.Equal("Open latest release", result["label"]);
    }

    private static IDictionary<string, object?> ResolveDownloadState(object? release)
    {
        var engine = new Engine();
        var script = File.ReadAllText(GetRepoPath("website/assets/site.js"));
        engine.SetValue("__release", release);
        engine.SetValue("__fallbackUrl", FallbackUrl);
        engine.Execute(script);

        var result = engine.Evaluate("ScreensViewSite.resolveDownloadState(__release, __fallbackUrl)");
        var raw = result.ToObject();
        var expando = Assert.IsType<ExpandoObject>(raw);

        return (IDictionary<string, object?>)expando;
    }

    private static string GetRepoPath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath));

    private const string FallbackUrl = "https://github.com/titanrain/ScreensView/releases";
}
