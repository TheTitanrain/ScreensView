using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using ScreensView.Shared;

namespace ScreensView.Agent.Legacy;

internal sealed class ScreenshotService
{
    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        SECURITY_IMPERSONATION_LEVEL impersonationLevel,
        TOKEN_TYPE tokenType,
        out IntPtr phNewToken);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    private enum SECURITY_IMPERSONATION_LEVEL { SecurityImpersonation = 2 }
    private enum TOKEN_TYPE { TokenPrimary = 1 }

    private const uint TOKEN_ALL_ACCESS = 0x000F01FF;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint STARTF_USESHOWWINDOW = 0x00000001;
    private const short SW_HIDE = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize;
        public uint dwXCountChars, dwYCountChars, dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public uint dwProcessId, dwThreadId;
    }

    private readonly AgentOptions _options;

    public ScreenshotService(AgentOptions options)
    {
        _options = options;
    }

    public byte[] CaptureJpeg()
    {
        uint sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
            throw new NoActiveSessionException();

        if (!WTSQueryUserToken(sessionId, out var hImpToken))
        {
            int err = Marshal.GetLastWin32Error();
            if (err == 1314)
                throw new InvalidOperationException(
                    $"WTSQueryUserToken failed: {err} — service must run as LocalSystem.");
            throw new NoActiveSessionException();
        }

        try
        {
            if (!DuplicateTokenEx(hImpToken, TOKEN_ALL_ACCESS, IntPtr.Zero,
                    SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                    TOKEN_TYPE.TokenPrimary, out var hPrimToken))
                throw new InvalidOperationException(
                    $"DuplicateTokenEx failed: {Marshal.GetLastWin32Error()}");
            try
            {
                return CaptureViaHelper(hPrimToken);
            }
            finally
            {
                CloseHandle(hPrimToken);
            }
        }
        finally
        {
            CloseHandle(hImpToken);
        }
    }

    private byte[] CaptureViaHelper(IntPtr hToken)
    {
        var pipeName = "ScreensViewShot-" + Guid.NewGuid().ToString("N");

        var ps = new PipeSecurity();
        ps.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        using var pipeServer = new NamedPipeServerStream(
            pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            ps);

        var exePath = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot determine current executable path.");

        var cmdLine = $"\"{exePath}\" --screenshot-helper {pipeName} {_options.ScreenshotQuality}";

        var si = new STARTUPINFO
        {
            cb = Marshal.SizeOf(typeof(STARTUPINFO)),
            lpDesktop = "winsta0\\default",
            dwFlags = STARTF_USESHOWWINDOW,
            wShowWindow = SW_HIDE
        };

        if (!CreateProcessAsUser(hToken, null, cmdLine,
                IntPtr.Zero, IntPtr.Zero, false,
                CREATE_NO_WINDOW, IntPtr.Zero, null,
                ref si, out var pi))
            throw new InvalidOperationException(
                $"CreateProcessAsUser failed: {Marshal.GetLastWin32Error()}");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                pipeServer.WaitForConnectionAsync(cts.Token).Wait();
            }
            catch (AggregateException ae) when (ae.InnerException is OperationCanceledException)
            {
                WaitForSingleObject(pi.hProcess, 0);
                throw new TimeoutException(
                    "Screenshot helper did not connect within 15 seconds.");
            }

            var result = ScreenshotTransferProtocol.Read(pipeServer);
            if (result.Status == ScreenshotTransferStatus.Locked)
                throw new NoActiveSessionException(result.Message);

            if (result.JpegBytes == null || result.JpegBytes.Length == 0)
                throw new InvalidDataException("Screenshot helper returned an empty JPEG payload.");

            return result.JpegBytes;
        }
        finally
        {
            WaitForSingleObject(pi.hProcess, 5_000);
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
        }
    }
}
