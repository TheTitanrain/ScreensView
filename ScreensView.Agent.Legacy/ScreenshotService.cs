using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace ScreensView.Agent.Legacy;

internal sealed class ScreenshotService
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenWindowStation(string lpszWinSta, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseWindowStation(IntPtr hWinSta);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessWindowStation(IntPtr hWinSta);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetProcessWindowStation();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetThreadDesktop(uint dwThreadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const uint WinStaAllAccess = 0x37F;
    private const uint DesktopAllAccess = 0x01FF;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private readonly AgentOptions _options;

    public ScreenshotService(AgentOptions options)
    {
        _options = options;
    }

    public byte[] CaptureJpeg()
    {
        var hWinSta = OpenWindowStation("WinSta0", false, WinStaAllAccess);
        if (hWinSta == IntPtr.Zero)
            throw new InvalidOperationException($"OpenWindowStation failed: {Marshal.GetLastWin32Error()}");

        var hOldWinSta = GetProcessWindowStation();
        if (hOldWinSta == IntPtr.Zero)
        {
            CloseWindowStation(hWinSta);
            throw new InvalidOperationException($"GetProcessWindowStation failed: {Marshal.GetLastWin32Error()}");
        }

        SetProcessWindowStation(hWinSta);

        var hDesktop = OpenDesktop("Default", 0, false, DesktopAllAccess);
        if (hDesktop == IntPtr.Zero)
        {
            SetProcessWindowStation(hOldWinSta);
            CloseWindowStation(hWinSta);
            throw new InvalidOperationException($"OpenDesktop failed: {Marshal.GetLastWin32Error()}");
        }

        var hOldDesktop = GetThreadDesktop(GetCurrentThreadId());
        SetThreadDesktop(hDesktop);

        try
        {
            return CaptureAllScreens();
        }
        finally
        {
            SetThreadDesktop(hOldDesktop);
            CloseDesktop(hDesktop);
            SetProcessWindowStation(hOldWinSta);
            CloseWindowStation(hWinSta);
        }
    }

    private byte[] CaptureAllScreens()
    {
        var left = GetSystemMetrics(SmXVirtualScreen);
        var top = GetSystemMetrics(SmYVirtualScreen);
        var width = GetSystemMetrics(SmCxVirtualScreen);
        var height = GetSystemMetrics(SmCyVirtualScreen);

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("GetSystemMetrics returned zero screen dimensions - no interactive desktop available.");

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(left, top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        return EncodeJpeg(bitmap);
    }

    private byte[] EncodeJpeg(Bitmap bitmap)
    {
        var encoder = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
        using var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)_options.ScreenshotQuality);
        using var ms = new MemoryStream();
        bitmap.Save(ms, encoder, encoderParams);
        return ms.ToArray();
    }
}
