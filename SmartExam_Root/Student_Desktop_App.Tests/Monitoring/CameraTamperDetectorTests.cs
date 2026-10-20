using FluentAssertions;
using Student_Desktop_App.Core.Monitoring;
using Student_Desktop_App.Core.Readiness;

namespace Student_Desktop_App.Tests.Monitoring;

/// <summary>
/// Module 5 webcam monitoring: frame statistics on synthetic frames and the debounce state machine
/// (covered / frozen / unavailable raised once per episode, after a persistence time, cleared after an absence).
/// Also the Module 2 readiness camera rule that uses the same "covered" test.
/// </summary>
public class CameraTamperDetectorTests
{
    private const int W = FrameMetrics.Width, H = FrameMetrics.Height;
    private static readonly DateTime T0 = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    private static byte[] Uniform(byte value) => Enumerable.Repeat(value, W * H).ToArray();

    private static byte[] Scene(int seed)
    {
        // A textured scene: checkerboard + per-frame sensor noise.
        var random = new Random(seed);
        var frame = new byte[W * H];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
                frame[y * W + x] = (byte)Math.Clamp((((x / 8) + (y / 8)) % 2 == 0 ? 60 : 190) + random.Next(-4, 5), 0, 255);
        return frame;
    }

    // Live scene: different noise every frame. Frozen: the same frame again and again.
    private static CameraObservation Live(double seconds, int seed = 0) => Observe(seconds, Scene(seed + (int)(seconds * 10)), Scene(seed + (int)(seconds * 10) + 1));
    private static CameraObservation Frozen(double seconds) => Observe(seconds, Scene(7), Scene(7));
    private static CameraObservation Black(double seconds) => Observe(seconds, Uniform(3), Uniform(3));
    private static CameraObservation Grey(double seconds) => Observe(seconds, Uniform(128), Uniform(128));   // tape: bright but featureless
    private static CameraObservation NoFrame(double seconds) => new(T0.AddSeconds(seconds), false);

    private static CameraObservation Observe(double seconds, byte[] frame, byte[] previous) =>
        new(T0.AddSeconds(seconds), true, FrameMetrics.Mean(frame), FrameMetrics.LaplacianVariance(frame, W, H), FrameMetrics.MeanAbsDiff(frame, previous));

    [Fact]
    public void FrameMetrics_TellDarkFlatAndTexturedFramesApart()
    {
        FrameMetrics.Mean(Uniform(0)).Should().Be(0);
        FrameMetrics.LaplacianVariance(Uniform(200), W, H).Should().Be(0);
        FrameMetrics.LaplacianVariance(Scene(1), W, H).Should().BeGreaterThan(100);
        FrameMetrics.MeanAbsDiff(Scene(1), Scene(1)).Should().Be(0);
        FrameMetrics.MeanAbsDiff(Scene(1), Scene(2)).Should().BeGreaterThan(1, "sensor noise always changes a live picture");
        FrameMetrics.MeanAbsDiff(Scene(1), new byte[3]).Should().BeNull();

        CameraTamperDetector.LooksCovered(FrameMetrics.Mean(Uniform(3)), 0).Should().BeTrue();
        CameraTamperDetector.LooksCovered(128, FrameMetrics.LaplacianVariance(Uniform(128), W, H)).Should().BeTrue("tape is bright but has no detail");
        CameraTamperDetector.LooksCovered(FrameMetrics.Mean(Scene(1)), FrameMetrics.LaplacianVariance(Scene(1), W, H)).Should().BeFalse();
    }

    [Fact]
    public void Covered_IsRaisedAfter3s_Once_AndClearedAfter2sOfPicture()
    {
        var d = new CameraTamperDetector();
        d.Observe(Live(0)).Should().BeEmpty();
        d.Observe(Black(1)).Should().BeEmpty();
        d.Observe(Black(3.5)).Should().BeEmpty("2.5 s is not yet 3 s");
        var raised = d.Observe(Black(4.1));
        raised.Should().ContainSingle(t => t.Issue == CameraIssue.CameraCovered && t.Raised);
        d.Status.Should().Be("Covered");
        d.Observe(Grey(6)).Should().BeEmpty("still covered: no second event");

        d.Observe(Live(7)).Should().BeEmpty("one good frame is not enough");
        d.Observe(Live(9.1)).Should().ContainSingle(t => t.Issue == CameraIssue.CameraCovered && !t.Raised);
        d.Status.Should().Be("Ok");
    }

    [Fact]
    public void AHandPassingTheLens_NeverRaisesAnything()
    {
        var d = new CameraTamperDetector();
        var all = new List<CameraTransition>();
        for (var s = 0.0; s < 30; s += 0.7)
            all.AddRange(d.Observe((int)(s / 0.7) % 4 == 0 ? Black(s) : Live(s)));   // dark for one frame in four
        all.Should().BeEmpty();
    }

    [Fact]
    public void Frozen_IsRaisedAfter15sOfIdenticalFrames_ButNotForACoveredLens()
    {
        var d = new CameraTamperDetector();
        d.Observe(Live(0));
        d.Observe(Frozen(1)).Should().BeEmpty();
        d.Observe(Frozen(15)).Should().BeEmpty();
        d.Observe(Frozen(16.1)).Should().ContainSingle(t => t.Issue == CameraIssue.CameraFrozen && t.Raised);
        d.Status.Should().Be("Frozen");
        d.Observe(Live(17));
        d.Observe(Live(19.5)).Should().ContainSingle(t => t.Issue == CameraIssue.CameraFrozen && !t.Raised);

        // A black, still image is "covered", never "frozen".
        var covered = new CameraTamperDetector();
        var transitions = new List<CameraTransition>();
        for (var s = 0; s <= 30; s++) transitions.AddRange(covered.Observe(Black(s)));
        transitions.Should().ContainSingle().Which.Issue.Should().Be(CameraIssue.CameraCovered);
    }

    [Fact]
    public void Unavailable_IsRaisedAfter5sWithoutFrames_AndTakesPriorityInTheStatus()
    {
        var d = new CameraTamperDetector();
        d.Observe(Black(0));
        d.Observe(Black(3.1)).Should().ContainSingle(t => t.Issue == CameraIssue.CameraCovered);
        d.Observe(NoFrame(4)).Should().BeEmpty();
        d.Observe(NoFrame(9.1)).Should().ContainSingle(t => t.Issue == CameraIssue.CameraUnavailable && t.Raised);
        d.Status.Should().Be("Unavailable");
        d.IsActive(CameraIssue.CameraCovered).Should().BeTrue("nothing is known about the lens while no frame arrives");

        d.Observe(Live(10));
        var back = d.Observe(Live(12.1));
        back.Should().Contain(t => t.Issue == CameraIssue.CameraUnavailable && !t.Raised);
        back.Should().Contain(t => t.Issue == CameraIssue.CameraCovered && !t.Raised);
        d.Status.Should().Be("Ok");
    }

    // ── Module 2: the readiness camera check grabs a real frame ──────────────────────────────────

    [Fact]
    public void M2_U_09_CameraReadiness_NeedsARealUncoveredPicture()
    {
        ReadinessPolicy.EvaluateCamera(false, 0, null, null).Should().Match<ReadinessItemResult>(r => r.Passed && r.Skipped);
        ReadinessPolicy.EvaluateCamera(true, 0, null, null).Detail.Should().Be("No device found");

        var noPicture = ReadinessPolicy.EvaluateCamera(true, 1, "Integrated Webcam", new CameraProbeResult(false, Error: "could not open the camera"));
        noPicture.Passed.Should().BeFalse();
        noPicture.Detail.Should().Contain("no picture").And.Contain("could not open the camera");

        var covered = ReadinessPolicy.EvaluateCamera(true, 1, "Integrated Webcam", new CameraProbeResult(true, 4, 0.5));
        covered.Passed.Should().BeFalse();
        covered.Detail.Should().Contain("dark or blocked");

        var ok = ReadinessPolicy.EvaluateCamera(true, 1, "Integrated Webcam", new CameraProbeResult(true, 110, 400));
        ok.Passed.Should().BeTrue();
        ok.Detail.Should().Be("Integrated Webcam: picture OK");

        // WMI missed the device but a frame was grabbed: the picture is what counts.
        ReadinessPolicy.EvaluateCamera(true, 0, null, new CameraProbeResult(true, 110, 400)).Passed.Should().BeTrue();
    }
}
