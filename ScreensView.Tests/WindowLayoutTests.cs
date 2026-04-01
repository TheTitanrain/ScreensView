namespace ScreensView.Tests;

public class WindowLayoutTests
{
    [Fact]
    public void ComputersManagerWindow_HasEnoughDefaultWidthForToolbarActions()
    {
        var computersManagerXaml = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\Views\ComputersManagerWindow.xaml"));

        Assert.Contains("Width=\"900\"", computersManagerXaml);
        Assert.Contains("MinWidth=\"900\"", computersManagerXaml);
    }

    [Fact]
    public void RefreshNowButton_IsHostedInMainWindowToolbar()
    {
        var mainWindowXaml = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\MainWindow.xaml"));

        Assert.Contains("Command=\"{Binding RefreshNowCommand}\"", mainWindowXaml);
        Assert.Contains("Text=\" Обновить сейчас\"", mainWindowXaml);
    }

    [Fact]
    public void UpdateAllAgentsButton_IsHostedInComputersManagerWindowToolbar()
    {
        var mainWindowXaml = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\MainWindow.xaml"));
        var computersManagerXaml = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\Views\ComputersManagerWindow.xaml"));

        Assert.DoesNotContain("Text=\" Обновить агентов\"", mainWindowXaml);
        Assert.Contains("Click=\"UpdateAllAgents_Click\"", computersManagerXaml);
        Assert.Contains("Text=\" Обновить агентов\"", computersManagerXaml);
    }

    private static string GetRepoPath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath));
}
