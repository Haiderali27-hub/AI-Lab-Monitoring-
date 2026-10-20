using Student_Desktop_App.Core.Monitoring;

namespace Student_Desktop_App.Services.Monitoring;

/// <summary>
/// Module 5 — on-device webcam monitoring for exams that require a webcam. A background thread grabs a frame
/// about every 0.7 s (≈1.5 FPS on CPU; LabGuard's 30 FPS is not needed for these checks), feeds its statistics to
/// <see cref="CameraTamperDetector"/> and reports each covered / frozen / unavailable episode once. Webcam snapshots
/// are taken on the exam's random webcam interval and immediately when an issue is raised; they go to the same
/// encrypted upload spool as screen captures. Nothing is analysed or stored when the exam does not require a webcam.
/// The loop runs on its own thread during a locked-down exam, so nothing that goes wrong in it (a camera driver,
/// OpenCV, a full disk behind a handler) may escape: an unhandled exception there would end the whole exam app.
/// A failed iteration counts as "no frame" (the camera then shows as unavailable) and the loop carries on.
/// </summary>
public sealed class WebcamMonitorService : IDisposable
{
    public static readonly TimeSpan DefaultFrameInterval = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan ReopenInterval = TimeSpan.FromSeconds(5);

    private readonly CameraTamperDetector _detector;
    private readonly CaptureSchedule _snapshots;
    private readonly Func<IWebcamFrameSource> _sourceFactory;
    private readonly TimeSpan _frameInterval;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Thread? _thread;
    private int _faults;

    /// <param name="sourceFactory">Tests pass a fake camera; production uses <see cref="WebcamCapture"/>.</param>
    /// <param name="frameInterval">Time between grabs (default 0.7 s, about 1.5 FPS).</param>
    /// <param name="thresholds">Tamper thresholds (tests shorten the debounce times).</param>
    public WebcamMonitorService(int snapshotMeanSeconds, Func<IWebcamFrameSource>? sourceFactory = null,
        TimeSpan? frameInterval = null, CameraThresholds? thresholds = null)
    {
        _snapshots = new CaptureSchedule(snapshotMeanSeconds, () => DateTime.UtcNow, Random.Shared.NextDouble);
        _sourceFactory = sourceFactory ?? (() => new WebcamCapture());
        _frameInterval = frameInterval is { } interval && interval > TimeSpan.Zero ? interval : DefaultFrameInterval;
        _detector = new CameraTamperDetector(thresholds);
    }

    /// <summary>Failures inside the loop that were absorbed instead of ending the thread (diagnostics and tests).</summary>
    public int Faults => Volatile.Read(ref _faults);

    /// <summary>An issue started or ended (raised on the monitoring thread).</summary>
    public event Action<CameraTransition>? IssueChanged;

    /// <summary>A webcam snapshot to upload: JPEG bytes, trigger (Interval | Violation) and reason.</summary>
    public event Action<byte[], string, string?>? SnapshotReady;

    public bool IsRunning => _thread is { IsAlive: true };

    /// <summary>Heartbeat value: Off when not running, otherwise Ok / Covered / Frozen / Unavailable.</summary>
    public string Status
    {
        get
        {
            if (!IsRunning) return "Off";
            lock (_gate) return _detector.Status;
        }
    }

    public void UpdateSnapshotInterval(int meanSeconds)
    {
        lock (_gate) _snapshots.UpdateMean(meanSeconds);
    }

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _thread = new Thread(() => Run(token)) { IsBackground = true, Name = "SmartExam webcam monitor" };
        _thread.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(3));
        _thread = null;
        _cts?.Dispose();
        _cts = null;
    }

    private void Run(CancellationToken token)
    {
        IWebcamFrameSource? camera = null;
        try
        {
            camera = _sourceFactory();
            byte[]? previous = null;
            var nextOpenAttempt = DateTime.MinValue;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    previous = Iterate(camera, previous, ref nextOpenAttempt);
                }
                catch (Exception)
                {
                    // Never let the monitoring thread die (or take the app with it); start over with a fresh open.
                    Interlocked.Increment(ref _faults);
                    previous = null;
                    TryClose(camera);
                }
                token.WaitHandle.WaitOne(_frameInterval);
            }
        }
        catch (Exception)
        {
            // The camera wrapper itself could not be created: webcam monitoring is off for this exam, the exam goes on.
            Interlocked.Increment(ref _faults);
        }
        finally
        {
            try
            {
                camera?.Dispose();
            }
            catch (Exception)
            {
                // releasing a broken driver must not crash on the way out either
            }
        }
    }

    // One frame: grab, judge, report transitions, maybe hand out a snapshot. Returns the frame the next one is compared with.
    private byte[]? Iterate(IWebcamFrameSource camera, byte[]? previous, ref DateTime nextOpenAttempt)
    {
        var now = DateTime.UtcNow;
        WebcamFrame? frame = null;
        try
        {
            if (!camera.IsOpen && now >= nextOpenAttempt)
            {
                nextOpenAttempt = now + ReopenInterval;
                camera.Open();
            }
            if (camera.IsOpen)
            {
                frame = camera.Grab();
                if (frame is null) camera.Close();   // reopen on the next attempt (unplugged / taken)
            }
        }
        catch (Exception)
        {
            // No OpenCV runtime, a broken driver, a disposed handle: the camera counts as unavailable, the exam goes on.
            Interlocked.Increment(ref _faults);
            frame = null;
            TryClose(camera);
        }

        var observation = frame is null
            ? new CameraObservation(now, false)
            : new CameraObservation(now, true, frame.Mean, frame.LaplacianVariance,
                previous is null ? null : FrameMetrics.MeanAbsDiff(frame.Grey, previous));

        IReadOnlyList<CameraTransition> transitions;
        bool snapshotDue, violationSnapshot = false;
        string? reason = null;
        lock (_gate)
        {
            transitions = _detector.Observe(observation);
            var raised = transitions.FirstOrDefault(t => t.Raised);
            if (raised is not null && _snapshots.TryClaimViolationCapture())
            {
                violationSnapshot = true;
                reason = raised.Issue.ToString();
            }
            snapshotDue = _snapshots.TakeIntervalDue();
        }

        foreach (var t in transitions) SafeInvoke(() => IssueChanged?.Invoke(t));

        // An unavailable camera has no picture to send; a covered one does (it shows the cover: that is the evidence).
        if ((violationSnapshot || snapshotDue) && frame is not null)
        {
            byte[]? jpeg = null;
            try
            {
                jpeg = camera.LastFrameJpeg();
            }
            catch (Exception)
            {
                Interlocked.Increment(ref _faults);   // skip this snapshot
            }
            if (jpeg is not null) SafeInvoke(() => SnapshotReady?.Invoke(jpeg, violationSnapshot ? "Violation" : "Interval", reason));
        }

        return frame?.Grey;
    }

    // A subscriber that throws (the spool on a full disk, ...) loses that one notification, not the monitoring thread.
    private void SafeInvoke(Action notify)
    {
        try
        {
            notify();
        }
        catch (Exception)
        {
            Interlocked.Increment(ref _faults);
        }
    }

    private static void TryClose(IWebcamFrameSource? camera)
    {
        try
        {
            camera?.Close();
        }
        catch (Exception)
        {
            // already broken
        }
    }

    public void Dispose() => Stop();
}
