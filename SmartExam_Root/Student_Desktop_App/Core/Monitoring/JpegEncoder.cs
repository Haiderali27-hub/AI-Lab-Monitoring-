using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Student_Desktop_App.Core.Monitoring;

/// <summary>
/// Module 5 — turns a capture into the JPEG that is uploaded: scaled so its longest side is at most
/// <see cref="CaptureSchedule.MaxSide"/> (1280 px) and encoded at quality <see cref="CaptureSchedule.JpegQuality"/> (60),
/// about 50–200 KB for a desktop. WPF imaging only (WIC; no System.Drawing dependency). Safe on any thread for a
/// FROZEN source; freezing is thread-affine, so a caller that encodes elsewhere freezes the bitmap on the thread that
/// created it first (ScreenCapture does; an unfrozen source is frozen here, which only works on its own thread). No screen access here, so it is unit-tested on synthetic bitmaps (M5-U-05).
/// </summary>
public static class JpegEncoder
{
    public static byte[] Encode(BitmapSource source, int maxSide = CaptureSchedule.MaxSide, int quality = CaptureSchedule.JpegQuality)
    {
        if (source.CanFreeze && !source.IsFrozen) source.Freeze();
        var (width, height) = CaptureSchedule.TargetSize(source.PixelWidth, source.PixelHeight, maxSide);

        BitmapSource scaled = source;
        if (width != source.PixelWidth || height != source.PixelHeight)
        {
            scaled = new TransformedBitmap(source, new ScaleTransform(
                (double)width / source.PixelWidth, (double)height / source.PixelHeight));
            scaled.Freeze();
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
        encoder.Frames.Add(BitmapFrame.Create(scaled));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    /// <summary>Encodes raw 8-bit greyscale pixels (a webcam frame) as JPEG.</summary>
    public static byte[] EncodeGrey(byte[] pixels, int width, int height, int quality = CaptureSchedule.JpegQuality) =>
        Encode(BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, pixels, width), int.MaxValue, quality);

    /// <summary>Encodes raw 24-bit BGR pixels (an OpenCV colour frame) as JPEG, scaled to the usual maximum.</summary>
    public static byte[] EncodeBgr24(byte[] pixels, int width, int height, int stride, int quality = CaptureSchedule.JpegQuality) =>
        Encode(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, stride), CaptureSchedule.MaxSide, quality);
}
