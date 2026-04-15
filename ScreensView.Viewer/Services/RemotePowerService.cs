using System.Management;

namespace ScreensView.Viewer.Services;

internal static class RemotePowerService
{
    public static Task RestartAsync(string host, string? username, string? password) =>
        Win32ShutdownAsync(host, username, password, flags: 6);

    public static Task ShutdownAsync(string host, string? username, string? password) =>
        Win32ShutdownAsync(host, username, password, flags: 12);

    private static Task Win32ShutdownAsync(string host, string? username, string? password, int flags) =>
        Task.Run(() =>
        {
            var options = username != null
                ? new ConnectionOptions
                  {
                      Username = username,
                      Password = password,
                      Impersonation = ImpersonationLevel.Impersonate,
                      Authentication = AuthenticationLevel.PacketPrivacy
                  }
                : new ConnectionOptions();

            var scope = new ManagementScope($@"\\{host}\root\cimv2", options);
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(scope, new SelectQuery("Win32_OperatingSystem"));
            foreach (ManagementObject os in searcher.Get())
                os.InvokeMethod("Win32Shutdown", new object[] { flags, 0 });
        });
}
