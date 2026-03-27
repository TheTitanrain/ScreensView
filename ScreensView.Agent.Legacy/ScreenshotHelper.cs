using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using ScreensView.Shared;

namespace ScreensView.Agent.Legacy;

internal static class ScreenshotHelper
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, IntPtr pvInfo, uint nLength, out uint lpnLengthNeeded);

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int UOI_NAME = 2;
    private const uint DESKTOP_READOBJECTS = 0x0001;
    private const uint READ_CONTROL = 0x00020000;
    private const string LockedDesktopMessage = "Workstation is locked or switched to a secure desktop.";

    internal static void Run(string pipeName, int quality)
    {
        NamedPipeClientStream? pipe = null;
        try
        {
            pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            pipe.Connect(5_000);

            if (IsSecureDesktopActive())
            {
                ScreenshotTransferProtocol.WriteLocked(pipe, LockedDesktopMessage);
                pipe.Flush();
                return;
            }

            int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("No display available.");

            using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(left, top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);

            var encoder = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
            using var ep = new EncoderParameters(1);
            ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
            using var ms = new MemoryStream();
            bmp.Save(ms, encoder, ep);

            ScreenshotTransferProtocol.WriteSuccess(pipe, ms.ToArray());
            pipe.Flush();
        }
        catch
        {
            if (pipe != null && pipe.IsConnected && TryWriteLockedResult(pipe))
                return;

            Environment.Exit(2);
        }
        finally
        {
            pipe?.Dispose();
        }
    }

    private static bool TryWriteLockedResult(Stream stream)
    {
        try
        {
            if (!IsSecureDesktopActive())
                return false;

            ScreenshotTransferProtocol.WriteLocked(stream, LockedDesktopMessage);
            stream.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSecureDesktopActive()
    {
        var desktop = OpenInputDesktop(0, false, DESKTOP_READOBJECTS | READ_CONTROL);
        if (desktop == IntPtr.Zero)
            return false;

        try
        {
            GetUserObjectInformation(desktop, UOI_NAME, IntPtr.Zero, 0, out var needed);
            if (needed == 0)
                return false;

            var buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!GetUserObjectInformation(desktop, UOI_NAME, buffer, needed, out _))
                    return false;

                var desktopName = Marshal.PtrToStringUni(buffer);
                return !string.Equals(desktopName, "Default", StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }
}
