using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace ScreensView.Agent;

internal static class ScreenshotHelper
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_XVIRTUALSCREEN  = 76;
    private const int SM_YVIRTUALSCREEN  = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    internal static void Run(string pipeName, int quality)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            pipe.Connect(5_000);

            int left   = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int top    = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int width  = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("No display available.");

            using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var g   = Graphics.FromImage(bmp);
            g.CopyFromScreen(left, top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);

            var encoder = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
            using var ep = new EncoderParameters(1);
            ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
            using var ms = new MemoryStream();
            bmp.Save(ms, encoder, ep);
            var jpeg = ms.ToArray();

            pipe.Write(BitConverter.GetBytes(jpeg.Length), 0, 4);
            pipe.Write(jpeg, 0, jpeg.Length);
            pipe.Flush();
        }
        catch
        {
            Environment.Exit(2);
        }
    }
}
