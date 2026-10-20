namespace Student_Desktop_App.Core.Monitoring;

/// <summary>What webcam monitoring can find. The names are the server's MonitoringEventType values.</summary>
public enum CameraIssue { CameraUnavailable, CameraCovered, CameraFrozen }

/// <summary>One processed webcam frame (or a failed grab: <see cref="FrameOk"/> false).</summary>
/// <param name="Diff">Mean absolute difference to the previous frame; null for the first frame.</param>
public sealed record CameraObservation(DateTime AtUtc, bool FrameOk, double Mean = 0, double LaplacianVariance = 0, double? Diff = null);

/// <summary>An issue started (<see cref="Raised"/>) or ended. One per episode, never one per frame.</summary>
public sealed record CameraTransition(CameraIssue Issue, bool Raised, DateTime AtUtc, string Detail);

/// <summary>Tunable limits; the defaults suit a 160×120 greyscale frame at 1–2 FPS.</summary>
public sealed record CameraThresholds
{
    /// <summary>Mean brightness below this is "dark" (lens covered, camera in a drawer).</summary>
    public double DarkMean { get; init; } = 25;
    /// <summary>Laplacian variance below this is "featureless" (finger or tape over the lens).</summary>
    public double FlatLaplacianVariance { get; init; } = 15;
    /// <summary>Frame-to-frame difference below this counts as "identical".</summary>
    public double FrozenDiff { get; init; } = 0.3;
    public TimeSpan CoveredRaiseAfter { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan FrozenRaiseAfter { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan UnavailableRaiseAfter { get; init; } = TimeSpan.FromSeconds(5);
    /// <summary>A raised issue clears only after the condition has been absent this long (no flapping).</summary>
    public TimeSpan ClearAfter { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Module 5 — the camera-tamper state machine (LabGuard defined such timeouts but never used them; this one is
/// wired). Each issue has a debounce: it is raised only after its condition held continuously for the issue's
/// "raise after" time, and cleared only after the condition was absent for <see cref="CameraThresholds.ClearAfter"/>,
/// so a hand passing in front of the lens or a dropped frame never produces an event. A covered lens also
/// produces identical black frames, so "frozen" is only judged on frames that are not covered. Pure (M5 webcam
/// unit tests); <c>Services/Monitoring/WebcamMonitorService</c> feeds it frames from OpenCV.
/// </summary>
public sealed class CameraTamperDetector
{
    private readonly CameraThresholds _t;
    private readonly Dictionary<CameraIssue, IssueState> _states = new()
    {
        [CameraIssue.CameraUnavailable] = new(),
        [CameraIssue.CameraCovered] = new(),
        [CameraIssue.CameraFrozen] = new()
    };

    public CameraTamperDetector(CameraThresholds? thresholds = null) => _t = thresholds ?? new CameraThresholds();

    /// <summary>Heartbeat value: Ok, or the most serious active issue (Unavailable, Covered, Frozen).</summary>
    public string Status =>
        IsActive(CameraIssue.CameraUnavailable) ? "Unavailable"
        : IsActive(CameraIssue.CameraCovered) ? "Covered"
        : IsActive(CameraIssue.CameraFrozen) ? "Frozen"
        : "Ok";

    public bool IsActive(CameraIssue issue) => _states[issue].Active;

    /// <summary>True when a frame with these statistics shows nothing usable (also the readiness rule).</summary>
    public static bool LooksCovered(double mean, double laplacianVariance, CameraThresholds? thresholds = null)
    {
        var t = thresholds ?? new CameraThresholds();
        return mean < t.DarkMean || laplacianVariance < t.FlatLaplacianVariance;
    }

    public IReadOnlyList<CameraTransition> Observe(CameraObservation o)
    {
        var transitions = new List<CameraTransition>();

        var covered = o.FrameOk && LooksCovered(o.Mean, o.LaplacianVariance, _t);
        // Identical frames only mean "frozen" when there is a picture to compare (a covered lens is black and still).
        bool? frozen = o.FrameOk && !covered && o.Diff is double d ? d < _t.FrozenDiff : null;

        Step(CameraIssue.CameraUnavailable, !o.FrameOk, _t.UnavailableRaiseAfter, o.AtUtc, transitions,
            "The webcam stopped delivering pictures");
        // While no frame arrives nothing is known about cover/freeze: keep their state as it is.
        if (o.FrameOk)
        {
            Step(CameraIssue.CameraCovered, covered, _t.CoveredRaiseAfter, o.AtUtc, transitions,
                $"Picture dark or featureless (brightness {o.Mean:0}, detail {o.LaplacianVariance:0})");
            if (frozen is bool isFrozen)
                Step(CameraIssue.CameraFrozen, isFrozen, _t.FrozenRaiseAfter, o.AtUtc, transitions,
                    "Webcam picture has not changed at all (looped image or virtual camera?)");
            else if (covered)
                Step(CameraIssue.CameraFrozen, false, _t.FrozenRaiseAfter, o.AtUtc, transitions, "");
        }
        return transitions;
    }

    private void Step(CameraIssue issue, bool condition, TimeSpan raiseAfter, DateTime at, List<CameraTransition> output, string detail)
    {
        var s = _states[issue];
        if (condition)
        {
            s.AbsentSince = null;
            s.PresentSince ??= at;
            if (!s.Active && at - s.PresentSince.Value >= raiseAfter)
            {
                s.Active = true;
                output.Add(new CameraTransition(issue, true, at, detail));
            }
        }
        else
        {
            s.PresentSince = null;
            if (!s.Active) return;
            s.AbsentSince ??= at;
            if (at - s.AbsentSince.Value >= _t.ClearAfter)
            {
                s.Active = false;
                s.AbsentSince = null;
                output.Add(new CameraTransition(issue, false, at, "Resolved"));
            }
        }
    }

    private sealed class IssueState
    {
        public bool Active;
        public DateTime? PresentSince;
        public DateTime? AbsentSince;
    }
}
