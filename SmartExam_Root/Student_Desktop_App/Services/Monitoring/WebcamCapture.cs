using System.Runtime.InteropServices;
using OpenCvSharp;
using Student_Desktop_App.Core.Monitoring;
using Student_Desktop_App.Core.Readiness;

namespace Student_Desktop_App.Services.Monitoring;

/// <summary>One webcam frame reduced to what monitoring needs: a 160×120 greyscale copy (see <see cref="FrameMetrics"/>).</summary>
public sealed record WebcamFrame(byte[] Grey, double Mean, double LaplacianVariance);

/// <summary>
/// Where <see cref="WebcamMonitorService"/> gets its frames: the real camera (<see cref="WebcamCapture"/>) or, in unit
/// tests, a fake — so the monitoring loop's resilience can be tested without a camera or OpenCV.
/// </summary>
public interface IWebcamFrameSource : IDisposable
{
    bool IsOpen { get; }
    bool Open(int index = 0);
    /// <summary>The next frame, or null when the camera delivered none.</summary>
    WebcamFrame? Grab();
    /// <summary>JPEG of the last grabbed frame; null before the first frame.</summary>
    byte[]? LastFrameJpeg();
    void Close();
}

/// <summary>
/// Module 2/5 — thin OpenCvSharp4 wrapper around the default webcam (DirectShow, 640×480). Only grabs frames and
/// shrinks them; every decision (covered, frozen, unavailable, readiness pass/fail) is made by the pure classes
/// in <c>Core</c>. Not thread-safe: one owner thread at a time.
/// </summary>
public sealed class WebcamCapture : IWebcamFrameSource
{
    private VideoCapture? _capture;
    private Mat? _lastColour;

    public bool IsOpen => _capture is { } c && c.IsOpened();

    public bool Open(int index = 0)
    {
        Close();
        var capture = new VideoCapture(index, VideoCaptureAPIs.DSHOW);
        if (!capture.IsOpened())
        {
            capture.Dispose();
            return false;
        }
        capture.Set(VideoCaptureProperties.FrameWidth, 640);
        capture.Set(VideoCaptureProperties.FrameHeight, 480);
        _capture = capture;
        return true;
    }

    /// <summary>The next frame, or null when the camera delivered none (unplugged, taken by another app).</summary>
    public WebcamFrame? Grab()
    {
        if (_capture is null) return null;
        var colour = new Mat();
        try
        {
            if (!_capture.Read(colour) || colour.Empty())
            {
                colour.Dispose();
                return null;
            }
        }
        catch (OpenCVException)
        {
            colour.Dispose();
            return null;
        }

        using var grey = new Mat();
        using var small = new Mat();
        switch (colour.Channels())
        {
            case 1: colour.CopyTo(grey); break;
            case 4: Cv2.CvtColor(colour, grey, ColorConversionCodes.BGRA2GRAY); break;
            default: Cv2.CvtColor(colour, grey, ColorConversionCodes.BGR2GRAY); break;
        }
        Cv2.Resize(grey, small, new Size(FrameMetrics.Width, FrameMetrics.Height), 0, 0, InterpolationFlags.Area);

        var pixels = new byte[FrameMetrics.Width * FrameMetrics.Height];
        if (small.IsContinuous() && small.Type() == MatType.CV_8UC1)
        {
            Marshal.Copy(small.Data, pixels, 0, pixels.Length);
        }
        else
        {
            for (var y = 0; y < FrameMetrics.Height; y++)
                Marshal.Copy(small.Ptr(y), pixels, y * FrameMetrics.Width, FrameMetrics.Width);
        }

        _lastColour?.Dispose();
        _lastColour = colour;
        return new WebcamFrame(pixels, FrameMetrics.Mean(pixels),
            FrameMetrics.LaplacianVariance(pixels, FrameMetrics.Width, FrameMetrics.Height));
    }

    /// <summary>JPEG (q60) of the last grabbed frame, for a webcam snapshot; null before the first frame.</summary>
    public byte[]? LastFrameJpeg()
    {
        if (_lastColour is null || _lastColour.Empty()) return null;
        Cv2.ImEncode(".jpg", _lastColour, out var jpeg, new ImageEncodingParam(ImwriteFlags.JpegQuality, CaptureSchedule.JpegQuality));
        return jpeg;
    }

    public void Close()
    {
        _lastColour?.Dispose();
        _lastColour = null;
        _capture?.Release();
        _capture?.Dispose();
        _capture = null;
    }

    public void Dispose() => Close();

    /// <summary>
    /// Module 2 readiness: opens the camera, lets auto-exposure settle (up to ~1.5 s of frames) and reports the last
    /// frame's statistics. Runs on a worker thread with a timeout, because some camera drivers hang on open.
    /// Never throws.
    /// </summary>
    public static async Task<CameraProbeResult> ProbeAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var work = Task.Run(() =>
        {
            try
            {
                using var camera = new WebcamCapture();
                if (!camera.Open()) return new CameraProbeResult(false, Error: "could not open the camera");
                WebcamFrame? last = null;
                for (var i = 0; i < 15 && !cancellationToken.IsCancellationRequested; i++)
                {
                    last = camera.Grab() ?? last;
                    if (last is not null && i >= 5 && !CameraTamperDetector.LooksCovered(last.Mean, last.LaplacianVariance)) break;
                    Thread.Sleep(100);
                }
                return last is null
                    ? new CameraProbeResult(false, Error: "no frames received")
                    : new CameraProbeResult(true, last.Mean, last.LaplacianVariance);
            }
            catch (Exception ex) when (ex is OpenCVException or DllNotFoundException or TypeInitializationException or BadImageFormatException)
            {
                return new CameraProbeResult(false, Error: ex.Message);
            }
        }, cancellationToken);

        var finished = await Task.WhenAny(work, Task.Delay(timeout, cancellationToken));
        return finished == work ? await work : new CameraProbeResult(false, Error: "the camera did not respond in time");
    }
}
