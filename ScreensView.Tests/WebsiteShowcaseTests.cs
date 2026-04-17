using System.Text.RegularExpressions;

namespace ScreensView.Tests;

public class WebsiteShowcaseTests
{
    [Fact]
    public void WebsiteTree_ContainsOnlyApprovedPublicPages()
    {
        var websiteDir = GetRepoPath("website");

        Assert.True(Directory.Exists(websiteDir));

        var htmlFiles = Directory.GetFiles(websiteDir, "*.html", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(websiteDir, path).Replace('\\', '/'))
            .OrderBy(path => path)
            .ToArray();

        Assert.Equal(
            [
                "en/index.html",
                "index.html"
            ],
            htmlFiles);
    }

    [Theory]
    [InlineData("website/index.html", "https://github.com/titanrain/ScreensView/blob/master/README.md")]
    [InlineData("website/en/index.html", "https://github.com/titanrain/ScreensView/blob/master/README.en.md")]
    public void ShowcasePages_LinkToLocaleMatchedDetailedGuide(string relativePath, string expectedReadmeUrl)
    {
        var html = File.ReadAllText(GetRepoPath(relativePath));

        Assert.Contains(expectedReadmeUrl, html);
    }

    [Theory]
    [InlineData("website/index.html", "en/")]
    [InlineData("website/en/index.html", "../")]
    public void ShowcasePages_UseRelativeLocaleSwitchLinks(string relativePath, string expectedLocaleHref)
    {
        var html = File.ReadAllText(GetRepoPath(relativePath));

        Assert.Contains($"href=\"{expectedLocaleHref}\"", html);
    }

    [Theory]
    [InlineData("website/index.html")]
    [InlineData("website/en/index.html")]
    public void ShowcasePages_DoNotUseRootRelativeInternalPaths(string relativePath)
    {
        var html = File.ReadAllText(GetRepoPath(relativePath));
        var rootRelativeHrefOrSrc = Regex.Matches(
            html,
            "(?:href|src)=\"/(?!/)[^\"]+\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        Assert.Empty(rootRelativeHrefOrSrc);
    }

    [Theory]
    [InlineData("website/index.html")]
    [InlineData("website/en/index.html")]
    public void ShowcasePages_UseMinimalLandingStructure(string relativePath)
    {
        var html = File.ReadAllText(GetRepoPath(relativePath));

        Assert.Contains("data-hero", html);
        Assert.Contains("data-capabilities", html);
        Assert.Contains("data-cta-row", html);
        Assert.Equal(3, Regex.Matches(html, "data-capability-card").Count);

        Assert.DoesNotContain("id=\"operations\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("id=\"faq\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hero-card", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ops-grid", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reliability-grid", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("website/index.html")]
    [InlineData("website/en/index.html")]
    public void ShowcasePages_ReserveScreenshotSlotInHero(string relativePath)
    {
        var html = File.ReadAllText(GetRepoPath(relativePath));

        Assert.Contains("data-hero", html);
        Assert.Contains("data-screenshot-slot", html);
    }

    [Fact]
    public void WebsiteRoot_ContainsNoJekyllMarker()
    {
        Assert.True(File.Exists(GetRepoPath("website/.nojekyll")));
    }

    [Fact]
    public void WebsiteDoesNotExposeInternalPlanningRoutes()
    {
        var websiteDir = GetRepoPath("website");
        var combinedText = string.Join(
            "\n",
            Directory.GetFiles(websiteDir, "*", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        Assert.DoesNotContain("docs/superpowers", combinedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/docs/", combinedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/en/docs/", combinedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Readmes_ContainTheRequiredDetailedGuideSections()
    {
        var russian = File.ReadAllText(GetRepoPath("README.ru.md"));
        var english = File.ReadAllText(GetRepoPath("README.md"));

        var requiredRussianHeadings = new[]
        {
            "## Требования",
            "## Быстрый старт",
            "### 1. Настройка агента",
            "### 5. Viewer",
            "### LLM-анализ экрана",
            "## Удалённая установка и обслуживание агента",
            "## Безопасность",
            "## Обновление",
            "### Настройки Viewer"
        };

        var requiredEnglishHeadings = new[]
        {
            "## Requirements",
            "## Quick Start",
            "### 1. Agent Configuration",
            "### 5. Viewer",
            "### LLM Screen Analysis",
            "## Remote Agent Installation and Maintenance",
            "## Security",
            "## Updates",
            "### Viewer Settings"
        };

        foreach (var heading in requiredRussianHeadings)
        {
            Assert.Contains(heading, russian);
        }

        foreach (var heading in requiredEnglishHeadings)
        {
            Assert.Contains(heading, english);
        }
    }

    [Fact]
    public void GitHubPagesWorkflow_PublishesWebsiteDirectory()
    {
        var workflow = File.ReadAllText(GetRepoPath(".github/workflows/pages.yml"));

        Assert.Contains("actions/configure-pages", workflow);
        Assert.Contains("actions/upload-pages-artifact", workflow);
        Assert.Contains("actions/deploy-pages", workflow);
        Assert.Contains("path: website", workflow);
    }

    private static string GetRepoPath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath));
}
