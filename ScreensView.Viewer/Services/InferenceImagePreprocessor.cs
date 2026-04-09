using System.IO;
using System.IO.Hashing;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreensView.Viewer.Services;

internal sealed record PreparedInferenceImage(
    byte[] JpegBytes,
    ulong Hash64,
    int Width,
    int Height);

internal static class InferenceImagePreprocessor
{
    internal const int MaxImageDimension = 768;
    internal const int JpegQuality = 80;

    public static Task<PreparedInferenceImage> PrepareAsync(BitmapSource screenshot)
    {
        if (screenshot.IsFrozen || screenshot.Dispatcher.CheckAccess())
            return Task.FromResult(Prepare(screenshot));

        return screenshot.Dispatcher.InvokeAsync(() => Prepare(screenshot)).Task;
    }

    public static PreparedInferenceImage Prepare(BitmapSource screenshot)
    {
        var prepared = DownscaleForInference(screenshot);
        var encoder = new JpegBitmapEncoder
        {
            QualityLevel = JpegQuality
        };
        encoder.Frames.Add(BitmapFrame.Create(prepared));
        using var ms = new MemoryStream();
        encoder.Save(ms);

        var jpegBytes = ms.ToArray();
        var hashBytes = XxHash64.Hash(jpegBytes);
        var hash = BitConverter.ToUInt64(hashBytes, 0);

        return new PreparedInferenceImage(
            jpegBytes,
            hash,
            prepared.PixelWidth,
            prepared.PixelHeight);
    }

    private static BitmapSource DownscaleForInference(BitmapSource screenshot)
    {
        var longestSide = Math.Max(screenshot.PixelWidth, screenshot.PixelHeight);
        if (longestSide <= MaxImageDimension)
            return screenshot;

        var scale = (double)MaxImageDimension / longestSide;
        var resized = new TransformedBitmap(screenshot, new ScaleTransform(scale, scale));
        resized.Freeze();
        return resized;
    }
}
