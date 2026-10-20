using System.IO;
using FluentAssertions;
using Student_Desktop_App.Core.Monitoring;
using Student_Desktop_App.Services.Monitoring;

namespace Student_Desktop_App.Tests.Monitoring;

/// <summary>
/// Module 5 (S5 verifier regression): the webcam monitoring loop runs on its own thread during a locked-down exam,
/// where an unhandled exception would end the whole exam app. A camera or OpenCV call that throws anything, and a
/// subscriber that throws (a full disk behind the capture spool), must be absorbed and the loop must keep running.
/// Driven by a fake camera, so no real webcam or OpenCV is touched.
/// </summary>
public class WebcamMonitorServiceTests
{
    private const int W = FrameMetrics.Width, H = FrameMetrics.Height;

    private sealed class FakeCamera : IWebcamFrameSource
    {
        private int _grabs;
        public Func<WebcamFrame?> OnGrab { get; init; } = () => null;
        public Func<byte[]?> OnJpeg { get; init; } = () => new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        public bool ThrowOnOpen { get; init; }
        public int Grabs => Volatile.Read(ref _grabs);
        public bool IsOpen { get; private set; }

        public bool Open(int index = 0)
        {
            if (ThrowOnOpen) throw new InvalidOperationException("driver exploded");
            IsOpen = true;
            return true;
        }

        public WebcamFrame? Grab()
        {
            Interlocked.Increment(ref _grabs);
            return OnGrab();
        }

        public byte[]? LastFrameJpeg() => OnJpeg();
        public void Close() { /* stays "open" so the next iteration grabs again without the 5 s reopen wait */ }
        public void Dispose() { }
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
    public async Task M5_U_15_CameraCallsThrowingAnything_DoNotEndTheMonitoringThread()
    {
        // InvalidOperationException is outside the OpenCV/DLL exceptions the loop used to filter for.
        var camera = new FakeCamera { OnGrab = () => throw new InvalidOperationException("handle disposed") };
        using var monitor = new WebcamMonitorService(60, () => camera, TimeSpan.FromMilliseconds(5));

        monitor.Start();
        await WaitUntil(() => camera.Grabs >= 5, "the loop must keep grabbing after a failing grab");

        monitor.IsRunning.Should().BeTrue();
        monitor.Faults.Should().BeGreaterThanOrEqualTo(5);
        monitor.Stop();
        monitor.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task M5_U_16_ThrowingSubscribers_LoseOneNotification_NotTheThread()
    {
        // A covered lens on every frame; the debounce is shortened so the issue is raised at once.
        var covered = new WebcamFrame(Enumerable.Repeat((byte)2, W * H).ToArray(), 2, 0);
        var camera = new FakeCamera { OnGrab = () => covered };
        var thresholds = new CameraThresholds { CoveredRaiseAfter = TimeSpan.Zero };
        using var monitor = new WebcamMonitorService(60, () => camera, TimeSpan.FromMilliseconds(5), thresholds);
        var issues = 0;
        var snapshots = 0;
        monitor.IssueChanged += _ =>
        {
            Interlocked.Increment(ref issues);
            throw new IOException("event outbox: disk full");
        };
        monitor.SnapshotReady += (_, _, _) =>
        {
            Interlocked.Increment(ref snapshots);
            throw new IOException("capture spool: disk full");
        };

        monitor.Start();
        await WaitUntil(() => Volatile.Read(ref snapshots) >= 1 && camera.Grabs >= 10,
            "the covered episode must be reported and the loop must keep running afterwards");

        Volatile.Read(ref issues).Should().Be(1, "one episode, one event, even though the subscriber threw");
        monitor.IsRunning.Should().BeTrue();
        monitor.Status.Should().Be("Covered");
        monitor.Stop();
    }

    [Fact]
    public async Task M5_U_17_ACameraThatCannotBeOpenedOrCreated_IsUnavailable_WithoutACrash()
    {
        var camera = new FakeCamera { ThrowOnOpen = true };
        using var broken = new WebcamMonitorService(60, () => camera, TimeSpan.FromMilliseconds(5));
        broken.Start();
        await WaitUntil(() => broken.Faults >= 1, "the failed open must be absorbed");
        broken.IsRunning.Should().BeTrue();
        broken.Stop();

        using var noCamera = new WebcamMonitorService(60, () => throw new DllNotFoundException("OpenCvSharpExtern"), TimeSpan.FromMilliseconds(5));
        noCamera.Start();
        await WaitUntil(() => noCamera.Faults >= 1, "a missing OpenCV runtime must be absorbed");
        noCamera.Stop();
        noCamera.Status.Should().Be("Off");
    }
}
