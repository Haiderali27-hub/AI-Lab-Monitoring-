using System.IO;
using System.Runtime.InteropServices;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using Student_Desktop_App.Core.Monitoring;

namespace Student_Desktop_App.Services.Monitoring;

/// <summary>
/// Module 5 (S8) — the on-device webcam models, CPU only, run through OpenCV's DNN module (already shipped with
/// OpenCvSharp4.runtime.win, so no extra runtime):
/// <list type="bullet">
/// <item>Faces: OpenCV Zoo YuNet <c>face_detection_yunet_2023mar.onnx</c> (MIT, 230 KB), on a 320-px-wide copy of the
/// frame — a few milliseconds. Decoded by <see cref="YuNetDecoder"/>.</item>
/// <item>Phones (optional): Megvii YOLOX-Nano <c>yolox_nano.onnx</c> (Apache-2.0, 3.6 MB, official 0.1.1rc0 release
/// asset), 416×416 letterbox, class 67 "cell phone". About 10× the face model's cost (≈ 150 ms on a 4-core lab PC
/// with two threads), so it runs on every <see cref="PhoneEveryNthFrame"/>-th frame only. Decoded by
/// <see cref="YoloxDecoder"/>.</item>
/// </list>
/// Both files live in <c>Models/</c> next to the exe (see <c>Models/MODELS.md</c> for source, hash and licence). A missing,
/// unreadable or wrong file only switches that check off: <see cref="Load"/> never throws, <see cref="Analyze"/> then
/// reports null for it, and a model that keeps failing at run time is dropped after
/// <see cref="MaxConsecutiveFailures"/> frames. Not thread-safe: one owner thread (the webcam monitor's, or the
/// readiness probe's own instance).
/// </summary>
public sealed class WebcamVision : IDisposable
{
    public const string FaceModelFile = "face_detection_yunet_2023mar.onnx";
    public const string PhoneModelFile = "yolox_nano.onnx";
    public const int FaceInputWidth = 320;
    public const int MaxConsecutiveFailures = 3;
    /// <summary>The phone model runs on every n-th analysed frame (≈ every 1.5 s at the monitor's rate); the frames in
    /// between report "not checked" (null), which the rule treats as "no change".</summary>
    public const int PhoneEveryNthFrame = 2;
    /// <summary>Phone scores below this are not even reported (the rule's own limit is higher).</summary>
    public const float PhoneReportFloor = 0.25f;

    private static readonly int[] PhoneClass = { YoloxDecoder.CocoCellPhone };

    private Net? _faceNet;
    private Net? _phoneNet;
    private int _faceFailures;
    private int _phoneFailures;
    private int _analysed;

    private WebcamVision(Net? faceNet, Net? phoneNet, IReadOnlyList<string> problems)
    {
        _faceNet = faceNet;
        _phoneNet = phoneNet;
        Problems = problems;
    }

    public static string DefaultModelsDirectory => Path.Combine(AppContext.BaseDirectory, "Models");

    /// <summary>An instance with nothing loaded (all checks off).</summary>
    public static WebcamVision None { get; } = new(null, null, Array.Empty<string>());

    public bool FaceDetectionAvailable => _faceNet is not null;
    public bool PhoneDetectionAvailable => _phoneNet is not null;

    /// <summary>Why a requested model is not available (diagnostics; empty when everything loaded).</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>Loads the requested models from <paramref name="modelsDirectory"/> (default <c>Models/</c> next to the exe). Never throws.</summary>
    public static WebcamVision Load(string? modelsDirectory = null, bool faces = true, bool phones = true)
    {
        var dir = modelsDirectory ?? DefaultModelsDirectory;
        var problems = new List<string>();
        try
        {
            // DNN inference is a short burst per frame; two threads keep the lab PC's IDE and compiler responsive.
            Cv2.SetNumThreads(2);
        }
        catch (Exception ex) when (IsNativeFailure(ex))
        {
            problems.Add($"OpenCV runtime unavailable: {ex.Message}");
            return new WebcamVision(null, null, problems);
        }
        var faceNet = faces ? TryLoad(Path.Combine(dir, FaceModelFile), problems) : null;
        var phoneNet = phones ? TryLoad(Path.Combine(dir, PhoneModelFile), problems) : null;
        return new WebcamVision(faceNet, phoneNet, problems);
    }

    private static Net? TryLoad(string path, List<string> problems)
    {
        if (!File.Exists(path))
        {
            problems.Add($"{Path.GetFileName(path)} not found");
            return null;
        }
        try
        {
            var net = CvDnn.ReadNetFromOnnx(path);
            if (net is null || net.Empty())
            {
                net?.Dispose();
                problems.Add($"{Path.GetFileName(path)} could not be read");
                return null;
            }
            net.SetPreferableBackend(Backend.OPENCV);
            net.SetPreferableTarget(Target.CPU);
            return net;
        }
        catch (Exception ex) when (ex is OpenCVException or IOException || IsNativeFailure(ex))
        {
            problems.Add($"{Path.GetFileName(path)} could not be loaded: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Faces and phone score for one camera frame (BGR, BGRA or grey); null when no model is loaded. The phone score
    /// is null on the frames the phone model skips (see <see cref="PhoneEveryNthFrame"/>).
    /// </summary>
    public VisionResult? Analyze(Mat frame)
    {
        if (_faceNet is null && _phoneNet is null) return null;
        using var bgr = ToBgr(frame);
        var faces = _faceNet is null ? null : Guard(ref _faceNet, ref _faceFailures, () => DetectFaces(_faceNet!, bgr));
        var checkPhone = _phoneNet is not null && _analysed++ % PhoneEveryNthFrame == 0;
        var phone = checkPhone ? Guard<float?>(ref _phoneNet, ref _phoneFailures, () => PhoneScore(_phoneNet!, bgr)) : null;
        return new VisionResult(faces, phone);
    }

    /// <summary>Faces only (the readiness check); null when the face model is not loaded.</summary>
    public IReadOnlyList<DetectedFace>? DetectFaces(Mat frame)
    {
        if (_faceNet is null) return null;
        using var bgr = ToBgr(frame);
        return Guard(ref _faceNet, ref _faceFailures, () => DetectFaces(_faceNet!, bgr));
    }

    // One model's run: a failure costs this frame's result; repeated failures (a wrong model file) switch it off.
    private static T? Guard<T>(ref Net? net, ref int failures, Func<T> run)
    {
        try
        {
            var result = run();
            failures = 0;
            return result;
        }
        catch (Exception ex) when (ex is OpenCVException or ArgumentException || IsNativeFailure(ex))
        {
            if (++failures >= MaxConsecutiveFailures)
            {
                net?.Dispose();
                net = null;
            }
            return default;
        }
    }

    private static IReadOnlyList<DetectedFace> DetectFaces(Net net, Mat bgr)
    {
        // 320 px wide keeps a face at a lab desk at 60+ px, YuNet's sweet spot, for a few ms of CPU.
        var width = FaceInputWidth;
        var height = Math.Max(1, (int)Math.Round((double)bgr.Rows * width / bgr.Cols));
        int padW = YuNetDecoder.PadToStride(width), padH = YuNetDecoder.PadToStride(height);

        using var resized = new Mat();
        Cv2.Resize(bgr, resized, new Size(width, height), 0, 0, InterpolationFlags.Linear);
        using var padded = new Mat(padH, padW, MatType.CV_8UC3, Scalar.All(0));
        using (var roi = new Mat(padded, new Rect(0, 0, width, height))) resized.CopyTo(roi);

        // YuNet takes raw BGR pixel values (no scaling, no mean).
        using var blob = CvDnn.BlobFromImage(padded, 1.0, new Size(padW, padH), new Scalar(0, 0, 0), false, false);
        net.SetInput(blob);
        var outputs = YuNetDecoder.OutputNames.Select(_ => new Mat()).ToArray();
        try
        {
            net.Forward(outputs, YuNetDecoder.OutputNames);
            var values = new Dictionary<string, float[]>(outputs.Length);
            for (var i = 0; i < outputs.Length; i++) values[YuNetDecoder.OutputNames[i]] = ToArray(outputs[i]);
            return YuNetDecoder.Decode(values, padW, padH, width, height);
        }
        finally
        {
            foreach (var m in outputs) m.Dispose();
        }
    }

    private static float PhoneScore(Net net, Mat bgr)
    {
        // YOLOX letterbox: keep the aspect ratio, top-left aligned, grey (114) padding, raw BGR values.
        var size = YoloxDecoder.InputSize;
        var ratio = Math.Min((float)size / bgr.Rows, (float)size / bgr.Cols);
        int w = Math.Max(1, (int)(bgr.Cols * ratio)), h = Math.Max(1, (int)(bgr.Rows * ratio));
        using var resized = new Mat();
        Cv2.Resize(bgr, resized, new Size(w, h), 0, 0, InterpolationFlags.Linear);
        using var padded = new Mat(size, size, MatType.CV_8UC3, Scalar.All(114));
        using (var roi = new Mat(padded, new Rect(0, 0, w, h))) resized.CopyTo(roi);

        using var blob = CvDnn.BlobFromImage(padded, 1.0, new Size(size, size), new Scalar(0, 0, 0), false, false);
        net.SetInput(blob);
        using var output = net.Forward();
        var detections = YoloxDecoder.Decode(ToArray(output), ratio, PhoneReportFloor, PhoneClass);
        return detections.Count == 0 ? 0f : detections.Max(d => d.Score);
    }

    private static Mat ToBgr(Mat frame)
    {
        var bgr = new Mat();
        switch (frame.Channels())
        {
            case 1: Cv2.CvtColor(frame, bgr, ColorConversionCodes.GRAY2BGR); break;
            case 4: Cv2.CvtColor(frame, bgr, ColorConversionCodes.BGRA2BGR); break;
            default: frame.CopyTo(bgr); break;
        }
        return bgr;
    }

    private static float[] ToArray(Mat m)
    {
        if (m.Type() != MatType.CV_32FC1 || !m.IsContinuous()) throw new ArgumentException($"Unexpected network output type {m.Type()}.");
        var values = new float[m.Total()];
        Marshal.Copy(m.Data, values, 0, values.Length);
        return values;
    }

    private static bool IsNativeFailure(Exception ex) =>
        ex is DllNotFoundException or TypeInitializationException or BadImageFormatException or EntryPointNotFoundException;

    public void Dispose()
    {
        _faceNet?.Dispose();
        _faceNet = null;
        _phoneNet?.Dispose();
        _phoneNet = null;
    }
}
