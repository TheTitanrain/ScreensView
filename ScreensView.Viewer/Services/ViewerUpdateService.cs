using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Windows;

namespace ScreensView.Viewer.Services;

public class ViewerUpdateService
{
    private const string GitHubReleasesUrl =
        "https://api.github.com/repos/titanrain/ScreensView/releases/latest";
    private const string UpdateDialogTitle = "Обновление ScreensView";

    public static async Task CheckAndUpdateAsync()
    {
        var args = Environment.GetCommandLineArgs();

        // === Running as the newly downloaded copy ===
        var updateFromIdx = Array.IndexOf(args, "--update-from");
        var installToIdx = Array.IndexOf(args, "--install-to");

        if (updateFromIdx >= 0 && installToIdx >= 0 &&
            updateFromIdx + 1 < args.Length && installToIdx + 1 < args.Length)
        {
            var oldPath = args[updateFromIdx + 1];
            var installPath = args[installToIdx + 1];
            var myPath = Environment.ProcessPath!;

            await Task.Delay(1500);

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    File.Copy(myPath, installPath, overwrite: true);
                    break;
                }
                catch
                {
                    await Task.Delay(500);
                }
            }

            Process.Start(new ProcessStartInfo(installPath) { UseShellExecute = true });

            try
            {
                File.Delete(oldPath);
            }
            catch
            {
            }

            Application.Current.Shutdown();
            return;
        }

        try
        {
            using var http = CreateHttpClient();
            var release = ToReleaseMetadata(await http.GetFromJsonAsync<GitHubRelease>(GitHubReleasesUrl));
            if (release == null)
            {
                return;
            }

            var latestVersion = ParseVersion(release.TagName);
            var currentVersion = GetCurrentVersion();
            if (latestVersion <= currentVersion || string.IsNullOrWhiteSpace(release.DownloadUrl))
            {
                return;
            }

            var result = MessageBox.Show(
                $"Доступна новая версия {release.TagName}.\nТекущая версия: {currentVersion}\n\nОбновить сейчас?",
                UpdateDialogTitle, MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            await DownloadAndLaunchUpdateAsync(http, release.DownloadUrl);
        }
        catch
        {
            // Update check is non-critical — silently ignore
        }
    }

    public static Task CheckManualAsync(Window? owner = null)
        => CheckManualAsync(owner, CreateManualCheckHooks(owner));

    internal static async Task CheckManualAsync(Window? owner, ManualCheckHooks hooks)
    {
        try
        {
            var release = await hooks.FetchReleaseAsync();
            if (release == null)
            {
                hooks.ShowMessage(new MessageRequest("Не удалось проверить обновления.", UpdateDialogTitle,
                    MessageBoxButton.OK, MessageBoxImage.Warning));
                return;
            }

            var latestVersion = ParseVersion(release.TagName);
            var currentVersion = hooks.GetCurrentVersion();

            if (latestVersion <= currentVersion)
            {
                hooks.ShowMessage(new MessageRequest("Вы используете последнюю версию.", UpdateDialogTitle,
                    MessageBoxButton.OK, MessageBoxImage.Information));
                return;
            }

            if (string.IsNullOrWhiteSpace(release.DownloadUrl))
            {
                hooks.ShowMessage(new MessageRequest("Не удалось проверить обновления.", UpdateDialogTitle,
                    MessageBoxButton.OK, MessageBoxImage.Warning));
                return;
            }

            var result = hooks.ShowMessage(new MessageRequest(
                $"Доступна новая версия {release.TagName}.\nТекущая версия: {currentVersion}\n\nОбновить сейчас?",
                UpdateDialogTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Information));
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            await hooks.LaunchUpdateAsync(release.DownloadUrl);
        }
        catch
        {
            hooks.ShowMessage(new MessageRequest("Не удалось проверить обновления.", UpdateDialogTitle,
                MessageBoxButton.OK, MessageBoxImage.Warning));
        }
    }

    private static ManualCheckHooks CreateManualCheckHooks(Window? owner)
    {
        return new ManualCheckHooks
        {
            FetchReleaseAsync = async () =>
            {
                using var http = CreateHttpClient();
                return ToReleaseMetadata(await http.GetFromJsonAsync<GitHubRelease>(GitHubReleasesUrl));
            },
            GetCurrentVersion = GetCurrentVersion,
            ShowMessage = request => MessageBox.Show(owner, request.Text, request.Caption, request.Buttons, request.Image),
            LaunchUpdateAsync = async downloadUrl =>
            {
                using var http = CreateHttpClient();
                await DownloadAndLaunchUpdateAsync(http, downloadUrl);
            }
        };
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        http.DefaultRequestHeaders.Add("User-Agent", "ScreensView");
        return http;
    }

    private static Version GetCurrentVersion()
        => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

    private static ReleaseMetadata? ToReleaseMetadata(GitHubRelease? release)
    {
        if (release == null)
        {
            return null;
        }

        var downloadUrl = release.Assets?.FirstOrDefault(a =>
            a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))?.BrowserDownloadUrl;

        return new ReleaseMetadata(release.TagName, downloadUrl);
    }

    private static async Task DownloadAndLaunchUpdateAsync(HttpClient http, string downloadUrl)
    {
        var originalPath = Environment.ProcessPath!;
        var tempPath = originalPath + ".download.exe";

        var bytes = await http.GetByteArrayAsync(downloadUrl);
        await File.WriteAllBytesAsync(tempPath, bytes);

        var launchArgs = $"--update-from \"{tempPath}\" --install-to \"{originalPath}\"";
        Process.Start(new ProcessStartInfo(tempPath, launchArgs) { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    internal static Version ParseVersion(string tag)
    {
        var clean = tag.TrimStart('v');
        return Version.TryParse(clean, out var v) ? v : new Version(0, 0);
    }

    internal sealed record ReleaseMetadata(string TagName, string? DownloadUrl);

    internal sealed record MessageRequest(string Text, string Caption, MessageBoxButton Buttons, MessageBoxImage Image);

    internal sealed class ManualCheckHooks
    {
        public required Func<Task<ReleaseMetadata?>> FetchReleaseAsync { get; init; }
        public required Func<Version> GetCurrentVersion { get; init; }
        public required Func<MessageRequest, MessageBoxResult> ShowMessage { get; init; }
        public required Func<string, Task> LaunchUpdateAsync { get; init; }
    }

    private class GitHubRelease
    {
        public string TagName { get; set; } = string.Empty;
        public List<GitHubAsset>? Assets { get; set; }
    }

    private class GitHubAsset
    {
        public string Name { get; set; } = string.Empty;
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}
