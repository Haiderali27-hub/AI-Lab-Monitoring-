using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Student_Desktop_App.Core.Monitoring;

namespace Student_Desktop_App.Tests.Monitoring;

/// <summary>M5-U-04…06 and the other pure Module 5 rules: process diff, capture schedule and size, batching, idle time.</summary>
public class MonitoringPolicyTests
{
    private DateTime _now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void M5_U_04_ProcessDiff_ReportsAddedAndRemovedApps()
    {
        var change = ProcessDiff.Compute(new[] { "SmartExam", "code" }, new[] { "SmartExam", "code", "chrome" });
        change.Added.Should().Equal("chrome");
        change.Removed.Should().BeEmpty();

        var back = ProcessDiff.Compute(new[] { "SmartExam", "chrome.exe" }, new[] { "smartexam", "Notepad" });
        back.Added.Should().Equal("Notepad");
        back.Removed.Should().Equal("chrome");   // case and ".exe" do not count as a change

        ProcessDiff.Compute(ProcessDiff.Parse("SmartExam,code"), ProcessDiff.Parse(" code , SmartExam ")).IsEmpty.Should().BeTrue();
        ProcessDiff.Parse("UNAVAILABLE").Should().BeEmpty();
    }

    [Theory]
    [InlineData(3840, 2160, 1280, 720)]    // 4K monitor
    [InlineData(3840, 1080, 1280, 360)]    // two 1080p monitors side by side (virtual screen)
    [InlineData(1080, 1920, 720, 1280)]    // portrait
    [InlineData(1280, 720, 1280, 720)]     // already small enough: unchanged
    [InlineData(800, 600, 800, 600)]
    public void M5_U_05_TargetSize_LongestSideAtMost1280_AspectKept(int w, int h, int ew, int eh) =>
        CaptureSchedule.TargetSize(w, h).Should().Be((ew, eh));

    [Fact]
    public void M5_U_05_A4KCapture_IsEncodedAsAJpegAtMost1280Wide()
    {
        var pixels = new byte[3840 * 2160 * 3];
        for (var i = 0; i < pixels.Length; i += 3) pixels[i] = (byte)(i / 3 % 256);   // a gradient, so JPEG has work to do
        var jpeg = JpegEncoder.EncodeBgr24(pixels, 3840, 2160, 3840 * 3);

        jpeg.Take(3).Should().Equal(new byte[] { 0xFF, 0xD8, 0xFF }, "the server only accepts real JPEG/PNG bytes");
        var decoded = BitmapDecoder.Create(new MemoryStream(jpeg), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        decoded.PixelWidth.Should().Be(1280);
        decoded.PixelHeight.Should().Be(720);
        jpeg.Length.Should().BeLessThan(1024 * 1024, "q60 at 1280 px stays far below the 10 MB upload limit");
    }

    [Fact]
    public void JpegEncoder_WorksOnAFrozenSourceOffTheUiThread()
    {
        var source = BitmapSource.Create(64, 48, 96, 96, PixelFormats.Gray8, null, new byte[64 * 48], 64);
        var jpeg = Task.Run(() => JpegEncoder.Encode(source)).GetAwaiter().GetResult();
        jpeg.Take(2).Should().Equal(new byte[] { 0xFF, 0xD8 });
        JpegEncoder.EncodeGrey(new byte[160 * 120], 160, 120).Take(2).Should().Equal(new byte[] { 0xFF, 0xD8 });
    }

    [Fact]
    public void CaptureSchedule_RandomInterval_IsHalfToOneAndAHalfTimesTheMean()
    {
        var schedule = new CaptureSchedule(60, () => _now, () => 0.0);
        schedule.NextInterval().Should().Be(TimeSpan.FromSeconds(30));
        var high = new CaptureSchedule(60, () => _now, () => 0.999999);
        high.NextInterval().TotalSeconds.Should().BeApproximately(90, 0.01);

        // Real randomness stays inside 30–90 s.
        var random = new Random(42);
        var real = new CaptureSchedule(60, () => _now, random.NextDouble);
        Enumerable.Range(0, 500).Select(_ => real.NextInterval().TotalSeconds).Should().OnlyContain(s => s >= 30 && s < 90);

        // Out-of-range means are clamped to the server's 10–600 s.
        new CaptureSchedule(1, () => _now, () => 0.5).MeanSeconds.Should().Be(10);
        new CaptureSchedule(0, () => _now, () => 0.5).MeanSeconds.Should().Be(60);
        new CaptureSchedule(5000, () => _now, () => 0.5).MeanSeconds.Should().Be(600);
    }

    [Fact]
    public void CaptureSchedule_IntervalCaptureComesDue_ThenReschedules()
    {
        var schedule = new CaptureSchedule(60, () => _now, () => 0.5);   // exactly 60 s
        schedule.TakeIntervalDue().Should().BeFalse();
        _now = _now.AddSeconds(59);
        schedule.TakeIntervalDue().Should().BeFalse();
        _now = _now.AddSeconds(1);
        schedule.TakeIntervalDue().Should().BeTrue();
        schedule.TakeIntervalDue().Should().BeFalse("the next one is a full interval away");
        schedule.NextDueUtc.Should().Be(_now.AddSeconds(60));

        // The teacher shortens the interval mid-exam: the sooner schedule wins.
        schedule.UpdateMean(20);
        schedule.NextDueUtc.Should().Be(_now.AddSeconds(20));
    }

    [Fact]
    public void CaptureSchedule_ViolationCaptures_AreRateLimitedTo1Per15s()
    {
        var schedule = new CaptureSchedule(60, () => _now, () => 0.5);
        schedule.TryClaimViolationCapture().Should().BeTrue();
        _now = _now.AddSeconds(10);
        schedule.TryClaimViolationCapture().Should().BeFalse();
        _now = _now.AddSeconds(5);
        schedule.TryClaimViolationCapture().Should().BeTrue();
    }

    [Fact]
    public void M5_U_06_Batcher_FlushesAt5sBoundaries_AndViolationsAtOnce()
    {
        var batcher = new EventBatcher(() => _now);
        batcher.IsDue(0, false).Should().BeFalse("nothing to send");
        batcher.IsDue(3, false).Should().BeFalse("not 5 s yet");
        _now = _now.AddSeconds(4.9);
        batcher.IsDue(3, false).Should().BeFalse();
        _now = _now.AddSeconds(0.1);
        batcher.IsDue(3, false).Should().BeTrue();
        batcher.MarkFlushed();

        _now = _now.AddSeconds(1);
        batcher.IsDue(1, false).Should().BeFalse();
        batcher.IsDue(1, urgentPending: true).Should().BeTrue("a violation is sent without waiting");
        batcher.MarkFlushed();
        _now = _now.AddSeconds(5);
        batcher.IsDue(1, false).Should().BeTrue("the next boundary is 5 s after the last flush");
    }

    [Fact]
    public void Batcher_BacksOffWhileOffline_AndReplaysWhenTheNetworkReturns()
    {
        var batcher = new EventBatcher(() => _now);
        _now = _now.AddSeconds(5);
        batcher.MarkFailed();
        batcher.CurrentWait.Should().Be(TimeSpan.FromSeconds(5));
        batcher.IsDue(1, urgentPending: true).Should().BeFalse("offline: violations wait for the backoff too");
        _now = _now.AddSeconds(5);
        batcher.IsDue(1, false).Should().BeTrue();
        batcher.MarkFailed();
        batcher.CurrentWait.Should().Be(TimeSpan.FromSeconds(10));
        for (var i = 0; i < 6; i++) batcher.MarkFailed();
        batcher.CurrentWait.Should().Be(EventBatcher.MaxBackoff);

        batcher.NetworkRestored();
        batcher.IsDue(1, false).Should().BeTrue("a successful heartbeat replays the backlog straight away");
        batcher.MarkFlushed();
        batcher.CurrentWait.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(10_000u, 4_000u, 6)]
    [InlineData(500u, 4_294_966_296u, 1)]       // tick counter wrapped (49.7 days) between input and now
    [InlineData(1_000u, 1_000u, 0)]
    public void IdleTime_HandlesTheTickCounterWrap(uint now, uint lastInput, int expected) =>
        IdleTime.Seconds(now, lastInput).Should().Be(expected);

    [Theory]
    [InlineData(200, DeliveryOutcome.Delivered)]
    [InlineData(null, DeliveryOutcome.Offline)]
    [InlineData(401, DeliveryOutcome.Offline)]
    [InlineData(429, DeliveryOutcome.Offline)]
    [InlineData(503, DeliveryOutcome.Offline)]
    [InlineData(400, DeliveryOutcome.Rejected)]
    [InlineData(404, DeliveryOutcome.Rejected)]
    [InlineData(409, DeliveryOutcome.Rejected)]      // session already over: drop the capture
    [InlineData(413, DeliveryOutcome.Rejected)]
    public void Delivery_ClassifiesServerAnswers(int? status, DeliveryOutcome expected) =>
        Delivery.Classify(status).Should().Be(expected);
}
