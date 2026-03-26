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
        "https://api.github.com/repos/YOUR_GITHUB_USER/ScreensView/releases/latest";

    public static async Task CheckAndUpdateAsync()
    {
        var args = Environment.GetCommandLineArgs();

        // === Running as the newly downloaded copy ===
        var updateFromIdx = Array.IndexOf(args, "--update-from");
        var installToIdx  = Array.IndexOf(args, "--install-to");

        if (updateFromIdx >= 0 && installToIdx >= 0 &&
            updateFromIdx + 1 < args.Length && installToIdx + 1 < args.Length)
        {
            var oldPath     = args[updateFromIdx + 1];
            var installPath = args[installToIdx + 1];
            var myPath      = Environment.ProcessPath!;

            // Wait for old process to release the file
            await Task.Delay(1500);

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    File.Copy(myPath, installPath, overwrite: true);
                    break;
                }
                catch { await Task.Delay(500); }
            }

            // Start the installed copy normally
            Process.Start(new ProcessStartInfo(installPath) { UseShellExecute = true });

            // Clean up temp download (best-effort)
            try { File.Delete(oldPath); } catch { }

            Application.Current.Shutdown();
            return;
        }

        // === Normal startup — check for update ===
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "ScreensView");
            http.Timeout = TimeSpan.FromSeconds(10);

            var release = await http.GetFromJsonAsync<GitHubRelease>(GitHubReleasesUrl);
            if (release == null) return;

            var latestVersion  = ParseVersion(release.TagName);
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
            if (latestVersion <= currentVersion) return;

            var downloadUrl = release.Assets?.FirstOrDefault(a =>
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))?.BrowserDownloadUrl;
            if (string.IsNullOrEmpty(downloadUrl)) return;

            var result = MessageBox.Show(
                $"Доступна новая версия {release.TagName}.\nТекущая версия: {currentVersion}\n\nОбновить сейчас?",
                "Обновление ScreensView", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (result != MessageBoxResult.Yes) return;

            var originalPath = Environment.ProcessPath!;
            var tempPath     = originalPath + ".download.exe";

            var bytes = await http.GetByteArrayAsync(downloadUrl);
            await File.WriteAllBytesAsync(tempPath, bytes);

            var launchArgs = $"--update-from \"{tempPath}\" --install-to \"{originalPath}\"";
            Process.Start(new ProcessStartInfo(tempPath, launchArgs) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch
        {
            // Update check is non-critical — silently ignore
        }
    }

    internal static Version ParseVersion(string tag)
    {
        var clean = tag.TrimStart('v');
        return Version.TryParse(clean, out var v) ? v : new Version(0, 0);
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
