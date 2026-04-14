using System.Xml.Linq;

namespace ScreensView.Tests;

public class SolutionFileTests
{
    [Fact]
    public void Repository_UsesSingleCanonicalSolutionFile()
    {
        var slnxPath = GetRepoPath("ScreensView.slnx");
        var slnPath = GetRepoPath("ScreensView.sln");

        Assert.True(File.Exists(slnxPath));
        Assert.False(
            File.Exists(slnPath),
            "The repository must not ship both ScreensView.sln and ScreensView.slnx because root-level dotnet commands become ambiguous.");
    }

    [Fact]
    public void Slnx_DeclaresStandardPlatformsExplicitly()
    {
        var slnxPath = GetRepoPath("ScreensView.slnx");
        var document = XDocument.Load(slnxPath);

        var platformNames = document.Root?
            .Element("Configurations")?
            .Elements("Platform")
            .Select(platform => (string?)platform.Attribute("Name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToArray();

        Assert.Equal(new[] { "Any CPU", "x64", "x86" }, platformNames);
    }

    private static string GetRepoPath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath));
}
