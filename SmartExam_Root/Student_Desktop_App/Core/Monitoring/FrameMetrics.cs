namespace Student_Desktop_App.Core.Monitoring;

/// <summary>
/// Module 5 — the three numbers webcam monitoring needs, computed on a small 8-bit greyscale frame (the camera
/// image is first scaled to <see cref="Width"/>×<see cref="Height"/>, so this costs well under a millisecond):
/// <list type="bullet">
/// <item><see cref="Mean"/> brightness (0–255): a covered lens or a dark room is near 0.</item>
/// <item><see cref="LaplacianVariance"/>: variance of the 4-neighbour Laplacian, i.e. how much edge detail the
/// image has. A finger or tape over the lens gives a flat, featureless image even when it is not dark.</item>
/// <item><see cref="MeanAbsDiff"/> between consecutive frames: a live camera always has some sensor noise, a looped
/// still image or a frozen virtual camera gives exactly the same bytes.</item>
/// </list>
/// Pure, no OpenCV: unit-tested on synthetic frames.
/// </summary>
public static class FrameMetrics
{
    public const int Width = 160;
    public const int Height = 120;

    public static double Mean(ReadOnlySpan<byte> grey)
    {
        if (grey.IsEmpty) return 0;
        long sum = 0;
        foreach (var b in grey) sum += b;
        return (double)sum / grey.Length;
    }

    public static double LaplacianVariance(ReadOnlySpan<byte> grey, int width, int height)
    {
        if (width < 3 || height < 3 || grey.Length < width * height) return 0;
        double sum = 0, sumSq = 0;
        var n = 0;
        for (var y = 1; y < height - 1; y++)
        {
            var row = y * width;
            for (var x = 1; x < width - 1; x++)
            {
                var i = row + x;
                double lap = grey[i - 1] + grey[i + 1] + grey[i - width] + grey[i + width] - 4 * grey[i];
                sum += lap;
                sumSq += lap * lap;
                n++;
            }
        }
        var mean = sum / n;
        return sumSq / n - mean * mean;
    }

    /// <summary>Mean absolute per-pixel difference; null when the frames are not comparable.</summary>
    public static double? MeanAbsDiff(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.IsEmpty || a.Length != b.Length) return null;
        long sum = 0;
        for (var i = 0; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]);
        return (double)sum / a.Length;
    }
}
