namespace ScreensView.Tests;

public class WindowLayoutTests
{
    [Fact]
    public void ComputersManagerWindow_HasEnoughDefaultWidthForToolbarActions()
    {
        var xaml = File.ReadAllText(GetRepoPath(@"ScreensView.Viewer\Views\ComputersManagerWindow.xaml"));

        Assert.DoesNotContain("MinWidth=\"900\"", xaml);
        Assert.Contains("x:Name=\"ToolbarPanel\"", xaml);
        Assert.Contains("x:Name=\"ConnectionsPanel\"", xaml);
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
