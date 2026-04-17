using ScreensView.Viewer.Services;
using System.Windows;

namespace ScreensView.Tests;

public class ViewerUpdateServiceTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("v0.1.0", 0, 1, 0)]
    [InlineData("v10.0.0", 10, 0, 0)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v1.2.3.4", 1, 2, 3, 4)]
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

    [Fact]
    public async Task CheckManualAsync_WhenReleaseIsMissing_ShowsWarning()
    {
        var shownMessages = new List<ViewerUpdateService.MessageRequest>();

        await ViewerUpdateService.CheckManualAsync(null, CreateHooks(
            fetchReleaseAsync: () => Task.FromResult<ViewerUpdateService.ReleaseMetadata?>(null),
            showMessage: request =>
            {
                shownMessages.Add(request);
                return MessageBoxResult.OK;
            }));

        var message = Assert.Single(shownMessages);
        Assert.Equal("Не удалось проверить обновления.", message.Text);
        Assert.Equal(MessageBoxImage.Warning, message.Image);
    }

    [Fact]
    public async Task CheckManualAsync_WhenCurrentVersionIsLatest_ShowsInformation()
    {
        var shownMessages = new List<ViewerUpdateService.MessageRequest>();

        await ViewerUpdateService.CheckManualAsync(null, CreateHooks(
            fetchReleaseAsync: () => Task.FromResult<ViewerUpdateService.ReleaseMetadata?>(
                new("v1.2.3", "https://example.invalid/ScreensView.exe")),
            currentVersion: () => new Version(1, 2, 3),
            showMessage: request =>
            {
                shownMessages.Add(request);
                return MessageBoxResult.OK;
            }));

        var message = Assert.Single(shownMessages);
        Assert.Equal("Вы используете последнюю версию.", message.Text);
        Assert.Equal(MessageBoxImage.Information, message.Image);
    }

    [Fact]
    public async Task CheckManualAsync_WhenReleaseHasNoExe_ShowsWarning()
    {
        var shownMessages = new List<ViewerUpdateService.MessageRequest>();

        await ViewerUpdateService.CheckManualAsync(null, CreateHooks(
            fetchReleaseAsync: () => Task.FromResult<ViewerUpdateService.ReleaseMetadata?>(
                new("v2.0.0", null)),
            currentVersion: () => new Version(1, 0, 0),
            showMessage: request =>
            {
                shownMessages.Add(request);
                return MessageBoxResult.OK;
            }));

        var message = Assert.Single(shownMessages);
        Assert.Equal("Не удалось проверить обновления.", message.Text);
        Assert.Equal(MessageBoxImage.Warning, message.Image);
    }

    [Fact]
    public async Task CheckManualAsync_WhenUserDeclines_DoesNotLaunchUpdate()
    {
        var shownMessages = new List<ViewerUpdateService.MessageRequest>();
        var launchedUrls = new List<string>();

        await ViewerUpdateService.CheckManualAsync(null, CreateHooks(
            fetchReleaseAsync: () => Task.FromResult<ViewerUpdateService.ReleaseMetadata?>(
                new("v2.0.0", "https://example.invalid/ScreensView.exe")),
            currentVersion: () => new Version(1, 0, 0),
            showMessage: request =>
            {
                shownMessages.Add(request);
                return MessageBoxResult.No;
            },
            launchUpdateAsync: release =>
            {
                launchedUrls.Add(release.DownloadUrl!);
                return Task.CompletedTask;
            }));

        var message = Assert.Single(shownMessages);
        Assert.Contains("Доступна новая версия v2.0.0.", message.Text);
        Assert.Equal(MessageBoxButton.YesNo, message.Buttons);
        Assert.Empty(launchedUrls);
    }

    [Fact]
    public async Task CheckManualAsync_WhenUserAccepts_LaunchesUpdateWithReleaseUrl()
    {
        var launchedUrls = new List<string>();

        await ViewerUpdateService.CheckManualAsync(null, CreateHooks(
            fetchReleaseAsync: () => Task.FromResult<ViewerUpdateService.ReleaseMetadata?>(
                new("v2.0.0", "https://example.invalid/ScreensView.exe")),
            currentVersion: () => new Version(1, 0, 0),
            showMessage: _ => MessageBoxResult.Yes,
            launchUpdateAsync: release =>
            {
                launchedUrls.Add(release.DownloadUrl!);
                return Task.CompletedTask;
            }));

        Assert.Equal(new[] { "https://example.invalid/ScreensView.exe" }, launchedUrls);
    }

    [Fact]
    public void GetPostUpdateRelaunchArguments_StripsInternalUpdateArgumentsAndPreservesConnectionsFile()
    {
        var args = new[]
        {
            @"C:\Temp\ScreensView.download.exe",
            "--update-from", @"C:\Temp\ScreensView.download.exe",
            "--install-to", @"C:\Program Files\ScreensView\ScreensView.Viewer.exe",
            "--connections-file", @"C:\Shared\connections.svc"
        };

        var relaunched = ViewerUpdateService.GetPostUpdateRelaunchArguments(args);

        Assert.Equal(["--connections-file", @"C:\Shared\connections.svc"], relaunched);
    }

    [Fact]
    public void BuildUpdaterLaunchArguments_ForwardsConnectionsFileArgument()
    {
        var result = ViewerUpdateService.BuildUpdaterLaunchArguments(
            @"C:\Temp\ScreensView.download.exe",
            @"C:\Program Files\ScreensView\ScreensView.Viewer.exe",
            ["--connections-file", @"C:\Shared Folder\connections.svc"]);

        Assert.Contains("--update-from", result, StringComparison.Ordinal);
        Assert.Contains("--install-to", result, StringComparison.Ordinal);
        Assert.Contains("--connections-file", result, StringComparison.Ordinal);
        Assert.Contains(@"""C:\Shared Folder\connections.svc""", result, StringComparison.Ordinal);
    }

    private static ViewerUpdateService.ManualCheckHooks CreateHooks(
        Func<Task<ViewerUpdateService.ReleaseMetadata?>> fetchReleaseAsync,
        Func<Version>? currentVersion = null,
        Func<ViewerUpdateService.MessageRequest, MessageBoxResult>? showMessage = null,
        Func<ViewerUpdateService.ReleaseMetadata, Task>? launchUpdateAsync = null)
    {
        return new ViewerUpdateService.ManualCheckHooks
        {
            FetchReleaseAsync = fetchReleaseAsync,
            GetCurrentVersion = currentVersion ?? (() => new Version(1, 0, 0)),
            ShowMessage = showMessage ?? (_ => MessageBoxResult.OK),
            LaunchUpdateAsync = launchUpdateAsync ?? (_ => Task.CompletedTask)
        };
    }
}
