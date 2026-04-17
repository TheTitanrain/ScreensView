using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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

            var relaunchArgs = BuildArgumentString(GetPostUpdateRelaunchArguments(args));
            Process.Start(new ProcessStartInfo(installPath, relaunchArgs) { UseShellExecute = true });

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

            await DownloadAndLaunchUpdateAsync(http, release.DownloadUrl, release.ChecksumUrl);
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

            await hooks.LaunchUpdateAsync(release);
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
            LaunchUpdateAsync = async release =>
            {
                using var http = CreateHttpClient();
                await DownloadAndLaunchUpdateAsync(http, release.DownloadUrl!, release.ChecksumUrl);
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

        var checksumUrl = release.Assets?.FirstOrDefault(a =>
            a.Name.Equals("checksums.sha256.txt", StringComparison.OrdinalIgnoreCase))?.BrowserDownloadUrl;

        return new ReleaseMetadata(release.TagName, downloadUrl, checksumUrl);
    }

    private static async Task DownloadAndLaunchUpdateAsync(HttpClient http, string downloadUrl, string? checksumUrl)
    {
        var originalPath = Environment.ProcessPath!;
        var tempPath = originalPath + ".download.exe";

        // Download installer bytes
        var bytes = await http.GetByteArrayAsync(downloadUrl);

        // Verify SHA-256 if release includes a checksums file (releases before this feature won't have it)
        if (!string.IsNullOrEmpty(checksumUrl))
        {
            var checksumText = await http.GetStringAsync(checksumUrl);
            var fileName = Uri.UnescapeDataString(downloadUrl.Split('/').Last());
            var expectedHash = ParseExpectedHash(checksumText, fileName);
            var actualHash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Update installer hash mismatch. Expected {expectedHash}, got {actualHash}.");
        }

        await File.WriteAllBytesAsync(tempPath, bytes);

        var launchArgs = BuildUpdaterLaunchArguments(tempPath, originalPath, Environment.GetCommandLineArgs().Skip(1));
        Process.Start(new ProcessStartInfo(tempPath, launchArgs) { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    // Parses "HASH  filename" lines (sha256sum convention)
    private static string ParseExpectedHash(string checksumText, string fileName)
    {
        foreach (var line in checksumText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && string.Equals(parts[1].Trim(), fileName, StringComparison.OrdinalIgnoreCase))
                return parts[0].Trim();
        }
        throw new InvalidDataException($"Checksum not found for '{fileName}' in checksums.sha256.txt.");
    }

    internal static IReadOnlyList<string> GetPostUpdateRelaunchArguments(IReadOnlyList<string> args)
    {
        var forwarded = new List<string>();

        for (var index = 1; index < args.Count; index++)
        {
            if (string.Equals(args[index], "--update-from", StringComparison.Ordinal) ||
                string.Equals(args[index], "--install-to", StringComparison.Ordinal))
            {
                if (index + 1 < args.Count)
                    index++;

                continue;
            }

            forwarded.Add(args[index]);
        }

        return forwarded;
    }

    internal static string BuildUpdaterLaunchArguments(
        string updateFromPath,
        string installToPath,
        IEnumerable<string> forwardedArgs)
    {
        return BuildArgumentString([
            "--update-from",
            updateFromPath,
            "--install-to",
            installToPath,
            .. forwardedArgs
        ]);
    }

    internal static string BuildArgumentString(IEnumerable<string> args) =>
        string.Join(" ", args.Select(QuoteArgument));

    private static string QuoteArgument(string argument)
    {
        if (argument.Length == 0)
            return "\"\"";

        var requiresQuotes = argument.Any(ch => char.IsWhiteSpace(ch) || ch == '"');
        if (!requiresQuotes)
            return argument;

        var builder = new StringBuilder(argument.Length + 2);
        builder.Append('"');
        var backslashCount = 0;

        foreach (var ch in argument)
        {
            if (ch == '\\')
            {
                backslashCount++;
                continue;
            }

            if (ch == '"')
            {
                builder.Append('\\', backslashCount * 2 + 1);
                builder.Append('"');
                backslashCount = 0;
                continue;
            }

            if (backslashCount > 0)
            {
                builder.Append('\\', backslashCount);
                backslashCount = 0;
            }

            builder.Append(ch);
        }

        if (backslashCount > 0)
            builder.Append('\\', backslashCount * 2);

        builder.Append('"');
        return builder.ToString();
    }

    internal static Version ParseVersion(string tag)
    {
        var clean = tag.TrimStart('v');
        return Version.TryParse(clean, out var v) ? v : new Version(0, 0);
    }

    internal sealed record ReleaseMetadata(string TagName, string? DownloadUrl, string? ChecksumUrl = null);

    internal sealed record MessageRequest(string Text, string Caption, MessageBoxButton Buttons, MessageBoxImage Image);

    internal sealed class ManualCheckHooks
    {
        public required Func<Task<ReleaseMetadata?>> FetchReleaseAsync { get; init; }
        public required Func<Version> GetCurrentVersion { get; init; }
        public required Func<MessageRequest, MessageBoxResult> ShowMessage { get; init; }
        public required Func<ReleaseMetadata, Task> LaunchUpdateAsync { get; init; }
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
