using System.Diagnostics;

namespace ScreensView.Viewer.Services;

internal static class RemotePowerService
{
    public static Task RestartAsync(string host, string? username, string? password) =>
        RunShutdownAsync(host, username, password, restart: true);

    public static Task ShutdownAsync(string host, string? username, string? password) =>
        RunShutdownAsync(host, username, password, restart: false);

    private static Task RunShutdownAsync(string host, string? username, string? password, bool restart) =>
        Task.Run(() =>
        {
            bool hasCredentials = !string.IsNullOrEmpty(username);
            if (hasCredentials)
                RunCommand("net", $@"use \\{host}\ipc$ /user:{username} {password}");

            try
            {
                string flag = restart ? "/r" : "/s";
                RunCommand("shutdown", $@"{flag} /m \\{host} /t 0 /f");
            }
            finally
            {
                if (hasCredentials)
                    RunCommand("net", $@"use \\{host}\ipc$ /delete", throwOnError: false);
            }
        });

    private static void RunCommand(string exe, string args, bool throwOnError = true)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (throwOnError && p.ExitCode != 0)
            throw new InvalidOperationException(p.StandardError.ReadToEnd().Trim());
    }
}
