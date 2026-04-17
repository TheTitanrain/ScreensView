using System;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace ScreensView.Agent.Legacy;

internal sealed class HttpsBindingManager
{
    private static readonly Guid AppId = new("8D9304E5-A73D-4E90-9B37-684A269B4019");

    public void EnsureBinding(int port, X509Certificate2 certificate)
    {
        var thumbprint = (certificate.Thumbprint ?? string.Empty).Replace(" ", string.Empty);
        if (string.IsNullOrWhiteSpace(thumbprint))
            throw new InvalidOperationException("Certificate thumbprint is empty.");

        RunNetsh($"http delete sslcert ipport=0.0.0.0:{port}", ignoreErrors: true);
        RunNetsh($"http add sslcert ipport=0.0.0.0:{port} certhash={thumbprint} certstorename=MY appid={AppId:B}");
        RunNetsh($"http delete urlacl url=https://+:{port}/", ignoreErrors: true);
        RunNetsh($"http add urlacl url=https://+:{port}/ user=\"NT AUTHORITY\\SYSTEM\"", ignoreErrors: true);
    }

    private static void RunNetsh(string arguments, bool ignoreErrors = false)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        process.Start();
        var stdOutTask = System.Threading.Tasks.Task.Run(() => process.StandardOutput.ReadToEnd());
        var stdErrTask = System.Threading.Tasks.Task.Run(() => process.StandardError.ReadToEnd());

        if (!process.WaitForExit(15_000))
        {
            process.Kill();
            throw new TimeoutException($"netsh timed out: {arguments}");
        }

        if (!System.Threading.Tasks.Task.WaitAll(new[] { stdOutTask, stdErrTask }, TimeSpan.FromSeconds(5)))
            throw new TimeoutException($"netsh stdout/stderr readers timed out: {arguments}");
        var stdOut = stdOutTask.Result;
        var stdErr = stdErrTask.Result;

        if (!ignoreErrors && process.ExitCode != 0)
            throw new InvalidOperationException($"netsh {arguments} failed with exit code {process.ExitCode}. Output: {stdOut} {stdErr}".Trim());
    }
}
