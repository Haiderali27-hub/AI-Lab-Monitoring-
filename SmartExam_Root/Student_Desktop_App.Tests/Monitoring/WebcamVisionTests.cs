using System.IO;
using FluentAssertions;
using OpenCvSharp;
using Student_Desktop_App.Core.Monitoring;
using Student_Desktop_App.Core.Readiness;
using Student_Desktop_App.Services.Monitoring;

namespace Student_Desktop_App.Tests.Monitoring;

/// <summary>
/// Module 5 (S8) — on-device webcam AI. The pure parts are tested with synthetic inputs: hand-made network output
/// tensors for the YuNet / YOLOX decoders (M5-U-18), landmarks of a generic face turned by known angles for the head
/// pose (M5-U-19), and detection sequences on a fake clock for the NoFace / MultipleFaces / LookingAway /
/// PhoneDetected debounce (M5-U-20). The monitoring loop is driven by a fake camera (M5-U-21). Model loading is tested
/// against a missing / corrupt file and against the shipped model files on a blank frame (M5-U-22) — no camera is
/// opened. The M2 readiness "exactly one face" rule is M2-U-10.
/// </summary>
public class WebcamVisionTests
{
    private static readonly DateTime T0 = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    // ---------- M5-U-18: decoders ----------

    private static Dictionary<string, float[]> EmptyYuNetOutputs(int width, int height)
    {
        var outputs = new Dictionary<string, float[]>();
        foreach (var stride in YuNetDecoder.Strides)
        {
            var cells = (width / stride) * (height / stride);
            outputs[$"cls_{stride}"] = new float[cells];
            outputs[$"obj_{stride}"] = new float[cells];
            outputs[$"bbox_{stride}"] = new float[cells * 4];
            outputs[$"kps_{stride}"] = new float[cells * 10];
        }
        return outputs;
    }

    private static void PutFace(Dictionary<string, float[]> o, int width, int stride, int row, int col, float cls, float obj,
        float dx, float dy, float logW, float logH, float kpDx = 0.1f, float kpDy = 0.2f)
    {
        var i = row * (width / stride) + col;
        o[$"cls_{stride}"][i] = cls;
        o[$"obj_{stride}"][i] = obj;
        o[$"bbox_{stride}"][i * 4] = dx;
        o[$"bbox_{stride}"][i * 4 + 1] = dy;
        o[$"bbox_{stride}"][i * 4 + 2] = logW;
        o[$"bbox_{stride}"][i * 4 + 3] = logH;
        for (var n = 0; n < 5; n++)
        {
            o[$"kps_{stride}"][i * 10 + 2 * n] = kpDx + n;
            o[$"kps_{stride}"][i * 10 + 2 * n + 1] = kpDy;
        }
    }

    [Fact]
    public void M5_U_18_YuNet_DecodesBoxesAndLandmarks_DropsWeakAndDuplicateFaces()
    {
        // A 640×480 camera frame shrunk to 320×240 and padded to 320×256, as WebcamVision does.
        const int w = 320, h = 256;
        var o = EmptyYuNetOutputs(w, h);
        // Face A at stride 16, cell (row 5, col 10): centre ((10+0.5)·16, (5+0.5)·16) = (168, 88), size 64×80.
        PutFace(o, w, 16, 5, 10, 0.9f, 0.9f, 0.5f, 0.5f, MathF.Log(4), MathF.Log(5));
        // The same face seen again by the stride-8 head with a lower score: suppressed.
        PutFace(o, w, 8, 11, 21, 0.85f, 0.85f, 0f, 0f, MathF.Log(8), MathF.Log(10));
        // Face B at stride 32 with the best score.
        PutFace(o, w, 32, 2, 2, 0.95f, 0.95f, 0.5f, 0.5f, 0f, 0f);
        // Weak cell: √(0.9·0.5) = 0.67 < 0.8.
        PutFace(o, w, 16, 10, 2, 0.9f, 0.5f, 0.5f, 0.5f, MathF.Log(3), MathF.Log(3));

        var faces = YuNetDecoder.Decode(o, w, h, 320, 240);

        faces.Should().HaveCount(2);
        faces[0].Score.Should().BeApproximately(0.95f, 1e-4f, "best first");
        var a = faces[1];
        a.Score.Should().BeApproximately(0.9f, 1e-4f);
        // Both axes in frame heights (240 px).
        a.X.Should().BeApproximately(136f / 240, 1e-4f);
        a.Y.Should().BeApproximately(48f / 240, 1e-4f);
        a.Width.Should().BeApproximately(64f / 240, 1e-4f);
        a.Height.Should().BeApproximately(80f / 240, 1e-4f);
        a.Landmarks.Should().HaveCount(5);
        a.RightEye.X.Should().BeApproximately((10 + 0.1f) * 16 / 240, 1e-4f);
        a.RightEye.Y.Should().BeApproximately((5 + 0.2f) * 16 / 240, 1e-4f);
        a.LeftMouth.X.Should().BeApproximately((10 + 4.1f) * 16 / 240, 1e-4f);

        YuNetDecoder.Decode(o, w, h, 320, 240, scoreThreshold: 0.6f).Should().HaveCount(3, "the weak face passes a lower threshold");
        YuNetDecoder.PadToStride(240).Should().Be(256);
        YuNetDecoder.OutputNames.Should().HaveCount(12).And.Contain(new[] { "cls_8", "obj_16", "bbox_32", "kps_8" });
    }

    [Fact]
    public void M5_U_18_YuNet_RejectsOutputsOfAnotherModel()
    {
        var o = EmptyYuNetOutputs(320, 256);
        YuNetDecoder.Decode(o, 320, 256, 320, 240).Should().BeEmpty();

        o.Remove("kps_32");
        FluentActions.Invoking(() => YuNetDecoder.Decode(o, 320, 256, 320, 240)).Should().Throw<ArgumentException>().WithMessage("*kps_32*");
        var wrongSize = EmptyYuNetOutputs(320, 320);
        FluentActions.Invoking(() => YuNetDecoder.Decode(wrongSize, 320, 256, 320, 240)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => YuNetDecoder.Decode(EmptyYuNetOutputs(320, 256), 320, 250, 320, 240)).Should().Throw<ArgumentException>();
    }

    private static float[] EmptyYolox() => new float[YoloxDecoder.RowCount() * (5 + YoloxDecoder.ClassCount)];

    private static void PutObject(float[] raw, int stride, int gy, int gx, float objectness, int classId, float classScore,
        float dx = 0.5f, float dy = 0.5f, float logW = 0.6931f, float logH = 1.3863f)
    {
        var row = 0;
        foreach (var s in YoloxDecoder.Strides)
        {
            var grid = YoloxDecoder.InputSize / s;
            if (s == stride) { row += gy * grid + gx; break; }
            row += grid * grid;
        }
        var o = row * (5 + YoloxDecoder.ClassCount);
        raw[o] = dx;
        raw[o + 1] = dy;
        raw[o + 2] = logW;
        raw[o + 3] = logH;
        raw[o + 4] = objectness;
        raw[o + 5 + classId] = classScore;
    }

    [Fact]
    public void M5_U_18_Yolox_DecodesAPhone_MapsBackToFramePixels_AndKeepsClassesApart()
    {
        YoloxDecoder.RowCount().Should().Be(3549, "52² + 26² + 13² anchor points for 416×416");
        var raw = EmptyYolox();
        // Phone at stride 16, cell (5, 10): centre (168, 88), size 32×64 in the input; frame was scaled by 0.65.
        PutObject(raw, 16, 5, 10, 0.9f, YoloxDecoder.CocoCellPhone, 0.8f);
        // A weaker duplicate of the same phone from the neighbouring cell: suppressed.
        PutObject(raw, 16, 5, 11, 0.9f, YoloxDecoder.CocoCellPhone, 0.6f, dx: -0.5f);
        // A person overlapping the phone: different class, kept.
        PutObject(raw, 8, 11, 21, 0.95f, YoloxDecoder.CocoPerson, 0.95f, dx: 0f, dy: 0f, logW: 2.0f, logH: 2.5f);
        // Objectness too low to matter.
        PutObject(raw, 32, 3, 3, 0.2f, YoloxDecoder.CocoCellPhone, 0.99f);

        var all = YoloxDecoder.Decode(raw, 0.65f, 0.3f);
        all.Select(d => d.ClassId).Should().BeEquivalentTo(new[] { YoloxDecoder.CocoPerson, YoloxDecoder.CocoCellPhone });

        var phones = YoloxDecoder.Decode(raw, 0.65f, 0.3f, new[] { YoloxDecoder.CocoCellPhone });
        var phone = phones.Should().ContainSingle().Subject;
        phone.Score.Should().BeApproximately(0.72f, 1e-4f, "objectness × class score");
        phone.Box.X.Should().BeApproximately((168 - 16) / 0.65f, 0.05f);
        phone.Box.Y.Should().BeApproximately((88 - 32) / 0.65f, 0.05f);
        phone.Box.Width.Should().BeApproximately(32 / 0.65f, 0.05f);
        phone.Box.Height.Should().BeApproximately(64 / 0.65f, 0.05f);

        YoloxDecoder.Decode(raw, 0.65f, 0.75f, new[] { YoloxDecoder.CocoCellPhone }).Should().BeEmpty("0.72 is under 0.75");
        FluentActions.Invoking(() => YoloxDecoder.Decode(new float[100], 0.65f, 0.3f)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void M5_U_18_IoUAndNms()
    {
        var a = new DetectionBox(0, 0, 10, 10);
        DetectionBox.IoU(a, a).Should().Be(1);
        DetectionBox.IoU(a, new DetectionBox(5, 0, 10, 10)).Should().BeApproximately(50f / 150, 1e-5f);
        DetectionBox.IoU(a, new DetectionBox(20, 20, 5, 5)).Should().Be(0);
        DetectionBox.Nms(new[] { a, new DetectionBox(1, 1, 10, 10), new DetectionBox(30, 30, 10, 10) }, new[] { 0.5f, 0.9f, 0.7f }, 0.3f)
            .Should().Equal(1, 2);
    }

    // ---------- M5-U-19: head pose ----------

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(30, 0, 0)]
    [InlineData(-45, 0, 0)]
    [InlineData(0, 30, 0)]
    [InlineData(0, -25, 0)]
    [InlineData(40, 25, 0)]
    [InlineData(-35, -20, 15)]
    [InlineData(20, 15, -20)]
    public void M5_U_19_HeadPose_RecoversYawAndPitch_WhateverTheRoll(double yaw, double pitch, double roll)
    {
        var pose = HeadPose.Estimate(HeadPose.Synthesize(yaw, pitch, roll));
        pose.Should().NotBeNull();
        pose!.Value.YawDegrees.Should().BeApproximately(yaw, 2.0);
        pose.Value.PitchDegrees.Should().BeApproximately(pitch, 2.0);
    }

    [Fact]
    public void M5_U_19_HeadPose_RefusesFacesTooSmallOrNonsensical()
    {
        HeadPose.Estimate(HeadPose.Synthesize(0, 0, eyeDistance: 0.02f)).Should().BeNull("eyes 0.02 frame heights apart ≈ 5 px");
        HeadPose.Estimate(HeadPose.Synthesize(0, 0, eyeDistance: 0.06f)).Should().NotBeNull();

        var upsideDown = HeadPose.Synthesize(0, 0, rollDegrees: 180);
        HeadPose.Estimate(upsideDown).Should().NotBeNull("an upside-down face is still a face once roll is undone");
        var f = HeadPose.Synthesize(0, 0);
        // Mouth drawn above the eyes, eyes swapped: no pose.
        var broken = f with { Landmarks = new[] { f.LeftEye, f.RightEye, f.Nose, f.RightMouth, f.LeftMouth } };
        HeadPose.Estimate(broken).Should().BeNull();
        HeadPose.Estimate(f with { Landmarks = new[] { f.RightEye, f.LeftEye } }).Should().BeNull();
    }

    // ---------- M5-U-20: face / phone debounce ----------

    private static DetectedFace Face(double yaw = 0, double pitch = 0, float centreX = 0.65f, float eyeDistance = 0.12f) =>
        HeadPose.Synthesize(yaw, pitch, centreX: centreX, eyeDistance: eyeDistance);

    private static VisionObservation At(double seconds, params DetectedFace[] faces) => new(T0.AddSeconds(seconds), faces);

    // Frames at 0.7 s like the monitor; returns every transition.
    private static List<CameraTransition> Feed(VisionIssueDetector d, double from, double to, Func<double, VisionObservation> frame)
    {
        var all = new List<CameraTransition>();
        for (var t = from; t <= to + 1e-9; t += 0.7) all.AddRange(d.Observe(frame(t)));
        return all;
    }

    [Fact]
    public void M5_U_20_NoFace_IsRaisedAfter5s_Once_AndClearedWhenTheStudentIsBack()
    {
        var d = new VisionIssueDetector();
        Feed(d, 0, 2, t => At(t, Face())).Should().BeEmpty();
        Feed(d, 2.1, 7.0, t => At(t)).Should().BeEmpty("4.9 s without a face is not yet 5 s");
        var raised = d.Observe(At(7.2));
        raised.Should().ContainSingle(t => t.Issue == CameraIssue.NoFace && t.Raised).Which.Detail.Should().Contain("No face");
        Feed(d, 7.9, 20, t => At(t)).Should().BeEmpty("one event per episode");
        d.Status.Should().Be("NoFace");

        d.Observe(At(21, Face())).Should().BeEmpty("cleared only after 2 s of the face being back");
        d.Observe(At(23.1, Face())).Should().ContainSingle(t => t.Issue == CameraIssue.NoFace && !t.Raised);
        d.Status.Should().Be("Ok");
    }

    [Fact]
    public void M5_U_20_MultipleFaces_After3s_SmallBackgroundFacesDoNotCount()
    {
        var d = new VisionIssueDetector();
        // A student two rows back: a face 0.06 frame heights tall. Not counted, no event.
        var far = Face(centreX: 0.2f, eyeDistance: 0.025f);
        far.Height.Should().BeLessThan(0.1f);
        Feed(d, 0, 10, t => At(t, Face(), far)).Should().BeEmpty();
        d.LastFaceCount.Should().Be(1);

        // Somebody leaning in next to the student.
        Feed(d, 10.5, 13.3, t => At(t, Face(), Face(centreX: 0.25f))).Should().BeEmpty("2.8 s");
        d.Observe(At(13.6, Face(), Face(centreX: 0.25f)))
            .Should().ContainSingle(t => t.Issue == CameraIssue.MultipleFaces && t.Raised).Which.Detail.Should().Be("2 faces in view");
        d.Status.Should().Be("MultipleFaces");
        d.IsActive(CameraIssue.LookingAway).Should().BeFalse();
    }

    [Fact]
    public void M5_U_20_LookingAway_RelativeToTheStudentsBaseline_After3s_AndAGlanceNeverCounts()
    {
        // Camera mounted beside the screen: looking at the screen shows as 18° yaw. That is this student's "straight".
        var d = new VisionIssueDetector();
        Feed(d, 0, 10, t => At(t, Face(yaw: 18, pitch: 5))).Should().BeEmpty();
        d.Baseline.Should().NotBeNull();
        d.Baseline!.Value.YawDegrees.Should().BeApproximately(18, 1);

        // 40° raw = 22° from the baseline: within the limit. A 2 s glance to 60°: too short.
        Feed(d, 10.5, 15, t => At(t, Face(yaw: 40))).Should().BeEmpty();
        Feed(d, 15.5, 17.4, t => At(t, Face(yaw: 60))).Should().BeEmpty();
        Feed(d, 17.5, 19, t => At(t, Face(yaw: 18))).Should().BeEmpty();

        // Turned to the neighbour's screen for over 3 s.
        var turned = Feed(d, 19.5, 23, t => At(t, Face(yaw: -25)));
        turned.Should().ContainSingle(t => t.Issue == CameraIssue.LookingAway && t.Raised)
            .Which.Detail.Should().Contain("turned sideways");
        d.Status.Should().Be("LookingAway");
        Feed(d, 23.5, 26, t => At(t, Face(yaw: 18))).Should().ContainSingle(t => t.Issue == CameraIssue.LookingAway && !t.Raised);

        // Looking down at the keyboard (+30° from baseline) is fine; looking up 25° for 3 s is not.
        Feed(d, 26.5, 31, t => At(t, Face(yaw: 18, pitch: 35))).Should().BeEmpty();
        Feed(d, 31.5, 35, t => At(t, Face(yaw: 18, pitch: -22)))
            .Should().ContainSingle(t => t.Issue == CameraIssue.LookingAway && t.Raised).Which.Detail.Should().Contain("looking up");
    }

    [Fact]
    public void M5_U_20_TheBaseline_IgnoresFramesWhereTheStudentIsTurnedAway()
    {
        var d = new VisionIssueDetector();
        // Turned away while the exam starts: not used for the baseline, so it cannot hide the turn.
        var early = Feed(d, 0, 4, t => At(t, Face(yaw: 45)));
        d.Baseline.Should().BeNull();
        early.Should().ContainSingle(t => t.Issue == CameraIssue.LookingAway && t.Raised, "relative to a straight-on camera until calibrated");
    }

    [Fact]
    public void M5_U_20_FramesWithoutAnalysis_AreNoEvidence()
    {
        var d = new VisionIssueDetector();
        d.Observe(At(0)).Should().BeEmpty();
        d.Observe(At(3)).Should().BeEmpty();
        // A covered camera (no analysis) for a moment: the 5 s count starts again afterwards.
        d.Observe(new VisionObservation(T0.AddSeconds(4), null)).Should().BeEmpty();
        d.Observe(At(6)).Should().BeEmpty("2 s since the evidence resumed, not 6 s");
        d.Observe(At(11.1)).Should().ContainSingle(t => t.Issue == CameraIssue.NoFace && t.Raised);
        // An active issue survives frames without analysis.
        d.Observe(new VisionObservation(T0.AddSeconds(15), null)).Should().BeEmpty();
        d.IsActive(CameraIssue.NoFace).Should().BeTrue();
    }

    [Fact]
    public void M5_U_20_Phone_NeedsTwoConsecutiveChecks_SkippedFramesChangeNothing()
    {
        var d = new VisionIssueDetector();
        var face = Face();
        VisionObservation Phone(double t, float? score) => new(T0.AddSeconds(t), new[] { face }, score);

        d.Observe(Phone(0, 0.8f)).Should().BeEmpty("one sighting is not enough");
        d.Observe(Phone(0.7, null)).Should().BeEmpty("frame the phone model skipped");
        d.Observe(Phone(1.4, 0.1f)).Should().BeEmpty("gone again: the episode never started");
        d.Observe(Phone(2.1, 0.7f)).Should().BeEmpty();
        d.Observe(Phone(2.8, null)).Should().BeEmpty();
        var raised = d.Observe(Phone(3.5, 0.6f));
        raised.Should().ContainSingle(t => t.Issue == CameraIssue.PhoneDetected && t.Raised).Which.Detail.Should().Contain("60%");
        d.Status.Should().Be("Phone");

        d.Observe(Phone(4.9, 0f)).Should().BeEmpty();
        d.Observe(Phone(7.0, 0.2f)).Should().ContainSingle(t => t.Issue == CameraIssue.PhoneDetected && !t.Raised, "below 0.45 counts as no phone");

        // Without a phone model the phone state never moves.
        var noModel = new VisionIssueDetector();
        Feed(noModel, 0, 10, t => new VisionObservation(T0.AddSeconds(t), new[] { face }, null)).Should().BeEmpty();
    }

    [Fact]
    public void M5_U_20_StatusPriority()
    {
        var fast = new VisionThresholds
        {
            NoFaceRaiseAfter = TimeSpan.Zero, MultipleFacesRaiseAfter = TimeSpan.Zero,
            LookingAwayRaiseAfter = TimeSpan.Zero, PhoneRaiseAfter = TimeSpan.Zero
        };
        var d = new VisionIssueDetector(fast);
        d.Status.Should().Be("Ok");
        d.Observe(new VisionObservation(T0, new[] { Face(yaw: 50) }, 0f));
        d.Status.Should().Be("LookingAway");
        d.Observe(new VisionObservation(T0.AddSeconds(1), Array.Empty<DetectedFace>(), 0f));
        d.Status.Should().Be("NoFace", "LookingAway is still clearing, NoFace ranks higher");
        d.Observe(new VisionObservation(T0.AddSeconds(2), new[] { Face(), Face(centreX: 0.2f) }, 0.9f));
        d.Status.Should().Be("MultipleFaces");
    }

    // ---------- M5-U-21: monitoring loop with a fake camera ----------

    private sealed class FakeVisionCamera : IWebcamFrameSource
    {
        private int _analyses;
        public Func<WebcamFrame?> OnGrab { get; init; } = () => null;
        public Func<VisionResult?> OnAnalyze { get; init; } = () => null;
        public int Analyses => Volatile.Read(ref _analyses);
        public bool IsOpen { get; private set; }
        public bool Open(int index = 0) => IsOpen = true;
        public WebcamFrame? Grab() => OnGrab();
        public byte[]? LastFrameJpeg() => new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };

        public VisionResult? AnalyzeLastFrame()
        {
            Interlocked.Increment(ref _analyses);
            return OnAnalyze();
        }

        public void Close() { }
        public void Dispose() { }
    }

    private static WebcamFrame TexturedFrame(int seed)
    {
        const int w = FrameMetrics.Width, h = FrameMetrics.Height;
        var random = new Random(seed);
        var pixels = new byte[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                pixels[y * w + x] = (byte)Math.Clamp((((x / 8) + (y / 8)) % 2 == 0 ? 60 : 190) + random.Next(-4, 5), 0, 255);
        return new WebcamFrame(pixels, FrameMetrics.Mean(pixels), FrameMetrics.LaplacianVariance(pixels, w, h));
    }

    private static async Task WaitUntil(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(because);
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task M5_U_21_MonitorRaisesMultipleFaces_WithAViolationSnapshot_AndReportsItAsStatus()
    {
        var seed = 0;
        var camera = new FakeVisionCamera
        {
            OnGrab = () => TexturedFrame(Interlocked.Increment(ref seed)),
            OnAnalyze = () => new VisionResult(new[] { Face(), Face(centreX: 0.2f) }, null)
        };
        using var monitor = new WebcamMonitorService(60, () => camera, TimeSpan.FromMilliseconds(5),
            visionThresholds: new VisionThresholds { MultipleFacesRaiseAfter = TimeSpan.Zero });
        var issues = new List<CameraTransition>();
        var snapshots = new List<(string Trigger, string? Reason)>();
        monitor.IssueChanged += t => { lock (issues) issues.Add(t); };
        monitor.SnapshotReady += (_, trigger, reason) => { lock (snapshots) snapshots.Add((trigger, reason)); };

        monitor.Start();
        await WaitUntil(() => { lock (snapshots) return snapshots.Count >= 1; }, "the raised issue must come with a webcam snapshot");
        await WaitUntil(() => camera.Analyses >= 5, "the loop keeps analysing");

        lock (issues) issues.Should().ContainSingle(t => t.Issue == CameraIssue.MultipleFaces && t.Raised);
        lock (snapshots) snapshots[0].Should().Be(("Violation", "MultipleFaces"));
        monitor.Status.Should().Be("MultipleFaces");
        monitor.Stop();
    }

    [Fact]
    public async Task M5_U_21_CoveredFrames_AreNotAnalysed_AndAThrowingModelIsAbsorbed()
    {
        var dark = new WebcamFrame(new byte[FrameMetrics.Width * FrameMetrics.Height], 0, 0);
        var covered = new FakeVisionCamera { OnGrab = () => dark, OnAnalyze = () => new VisionResult(Array.Empty<DetectedFace>(), 0f) };
        using (var monitor = new WebcamMonitorService(60, () => covered, TimeSpan.FromMilliseconds(5),
                   new CameraThresholds { CoveredRaiseAfter = TimeSpan.Zero },
                   new VisionThresholds { NoFaceRaiseAfter = TimeSpan.Zero }))
        {
            monitor.Start();
            await WaitUntil(() => monitor.Status == "Covered", "a dark lens is CameraCovered");
            await Task.Delay(50);
            covered.Analyses.Should().Be(0, "a covered lens is not \"no face\"");
            monitor.Stop();
        }

        var seed = 0;
        var throwing = new FakeVisionCamera
        {
            OnGrab = () => TexturedFrame(Interlocked.Increment(ref seed)),
            OnAnalyze = () => throw new InvalidOperationException("model exploded")
        };
        using var broken = new WebcamMonitorService(60, () => throwing, TimeSpan.FromMilliseconds(5),
            visionThresholds: new VisionThresholds { NoFaceRaiseAfter = TimeSpan.Zero });
        broken.Start();
        await WaitUntil(() => throwing.Analyses >= 5, "the loop keeps going after a failing model");
        broken.IsRunning.Should().BeTrue();
        broken.Faults.Should().BeGreaterThanOrEqualTo(5);
        broken.Status.Should().Be("Ok", "a failing model is no evidence of anything");
        broken.Stop();
    }

    // ---------- M5-U-22: model loading degrades gracefully ----------

    [Fact]
    public void M5_U_22_MissingOrCorruptModelFiles_SwitchTheChecksOff_WithoutThrowing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "smartexam-models-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using (var none = WebcamVision.Load(dir))
            {
                none.FaceDetectionAvailable.Should().BeFalse();
                none.PhoneDetectionAvailable.Should().BeFalse();
                none.Problems.Should().Contain(p => p.Contains(WebcamVision.FaceModelFile) && p.Contains("not found"));
                using var frame = new Mat(480, 640, MatType.CV_8UC3, Scalar.All(128));
                none.Analyze(frame).Should().BeNull("no model, no analysis: the rules see \"no evidence\"");
            }

            File.WriteAllText(Path.Combine(dir, WebcamVision.FaceModelFile), "this is not an onnx file");
            using var corrupt = WebcamVision.Load(dir, phones: false);
            corrupt.FaceDetectionAvailable.Should().BeFalse();
            corrupt.Problems.Should().ContainSingle(p => p.Contains(WebcamVision.FaceModelFile));

            using var capture = new WebcamCapture(WebcamVision.Load(dir));
            capture.AnalyzeLastFrame().Should().BeNull("no frame grabbed yet");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void M5_U_22_ShippedModels_LoadAndRunOnTheCpu()
    {
        // The csproj copies Models/*.onnx next to the exe (and so into this test's output folder).
        using var vision = WebcamVision.Load();
        vision.Problems.Should().BeEmpty();
        vision.FaceDetectionAvailable.Should().BeTrue();
        vision.PhoneDetectionAvailable.Should().BeTrue();

        // A plain grey 640×480 frame: the real networks run end to end and find nothing.
        using var grey = new Mat(480, 640, MatType.CV_8UC3, Scalar.All(128));
        var first = vision.Analyze(grey);
        first.Should().NotBeNull();
        first!.Faces.Should().BeEmpty();
        first.PhoneScore.Should().Be(0f);
        vision.Analyze(grey)!.PhoneScore.Should().BeNull("the phone model only runs on every other frame");

        // Grey-scale and BGRA camera formats are accepted too.
        using var mono = new Mat(240, 320, MatType.CV_8UC1, Scalar.All(90));
        vision.DetectFaces(mono).Should().BeEmpty();
        using var bgra = new Mat(480, 640, MatType.CV_8UC4, Scalar.All(200));
        vision.Analyze(bgra)!.Faces.Should().BeEmpty();
    }

    // ---------- M2-U-10: readiness needs exactly one face ----------

    [Fact]
    public void M2_U_10_CameraReadiness_NeedsExactlyOneFace_WhenTheFaceModelRan()
    {
        var good = new CameraProbeResult(true, 110, 400);
        ReadinessPolicy.EvaluateCamera(true, 1, "Integrated Webcam", good with { FaceCount = 1 })
            .Should().Match<ReadinessItemResult>(r => r.Passed && r.Detail == "Integrated Webcam: picture OK, face detected");

        var nobody = ReadinessPolicy.EvaluateCamera(true, 1, "Integrated Webcam", good with { FaceCount = 0 });
        nobody.Passed.Should().BeFalse();
        nobody.Detail.Should().Contain("No face visible");

        var crowd = ReadinessPolicy.EvaluateCamera(true, 1, "Integrated Webcam", good with { FaceCount = 3 });
        crowd.Passed.Should().BeFalse();
        crowd.Detail.Should().Contain("3 faces visible");

        // No face model on this PC: the picture rule alone decides (as before S8).
        ReadinessPolicy.EvaluateCamera(true, 1, "Integrated Webcam", good).Detail.Should().Be("Integrated Webcam: picture OK");
        // A covered lens still fails first, whatever the face count says.
        ReadinessPolicy.EvaluateCamera(true, 1, null, new CameraProbeResult(true, 3, 0, FaceCount: 1)).Passed.Should().BeFalse();
        // Not required: skipped.
        ReadinessPolicy.EvaluateCamera(false, 0, null, good with { FaceCount = 0 }).Skipped.Should().BeTrue();
    }

    [Fact]
    public void M2_U_10_FaceCount_UsesTheSameSizeRuleAsMonitoring()
    {
        VisionIssueDetector.CountFaces(new[] { Face(), Face(centreX: 0.2f, eyeDistance: 0.025f) }).Should().Be(1);
        VisionIssueDetector.CountFaces(Array.Empty<DetectedFace>()).Should().Be(0);
    }
}
