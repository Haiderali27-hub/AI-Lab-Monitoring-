using Student_Desktop_App.Core.Monitoring;

namespace Student_Desktop_App.Services.Monitoring;

/// <summary>
/// Module 5 — on-device webcam monitoring for exams that require a webcam. A background thread grabs a frame
/// about every 0.7 s (≈1.5 FPS on CPU; LabGuard's 30 FPS is not needed for these checks), feeds its statistics to
/// <see cref="CameraTamperDetector"/> and reports each covered / frozen / unavailable episode once. Webcam snapshots
/// are taken on the exam's random webcam interval and immediately when an issue is raised; they go to the same
/// encrypted upload spool as screen captures. Nothing is analysed or stored when the exam does not require a webcam.
/// </summary>
public sealed class WebcamMonitorService : IDisposable
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan ReopenInterval = TimeSpan.FromSeconds(5);

    private readonly CameraTamperDetector _detector = new();
    private readonly CaptureSchedule _snapshots;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Thread? _thread;

    public WebcamMonitorService(int snapshotMeanSeconds) =>
        _snapshots = new CaptureSchedule(snapshotMeanSeconds, () => DateTime.UtcNow, Random.Shared.NextDouble);

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
        using var camera = new WebcamCapture();
        byte[]? previous = null;
        var nextOpenAttempt = DateTime.MinValue;

        while (!token.IsCancellationRequested)
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
            catch (Exception ex) when (ex is OpenCvSharp.OpenCVException or DllNotFoundException or TypeInitializationException or BadImageFormatException)
            {
                // No OpenCV runtime or a broken driver: the camera counts as unavailable, the exam goes on.
                frame = null;
            }

            var observation = frame is null
                ? new CameraObservation(now, false)
                : new CameraObservation(now, true, frame.Mean, frame.LaplacianVariance,
                    previous is null ? null : FrameMetrics.MeanAbsDiff(frame.Grey, previous));
            if (frame is not null) previous = frame.Grey;
            else previous = null;

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

            foreach (var t in transitions) IssueChanged?.Invoke(t);

            // An unavailable camera has no picture to send; a covered one does (it shows the cover — that is the evidence).
            if ((violationSnapshot || snapshotDue) && frame is not null)
            {
                try
                {
                    var jpeg = camera.LastFrameJpeg();
                    if (jpeg is not null) SnapshotReady?.Invoke(jpeg, violationSnapshot ? "Violation" : "Interval", reason);
                }
                catch (OpenCvSharp.OpenCVException)
                {
                    // skip this snapshot
                }
            }

            token.WaitHandle.WaitOne(FrameInterval);
        }
    }

    public void Dispose() => Stop();
}
