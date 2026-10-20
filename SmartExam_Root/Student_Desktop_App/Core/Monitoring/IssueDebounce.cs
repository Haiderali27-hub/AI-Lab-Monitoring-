namespace Student_Desktop_App.Core.Monitoring;

/// <summary>
/// Module 5 — the debounce shared by every webcam finding (camera tamper and the S8 face / phone checks): an issue is
/// raised only after its condition held continuously for its "raise after" time, and cleared only after the
/// condition was absent for the "clear after" time, so a single odd frame never produces or ends an event.
/// One instance per issue. Pure; driven by the frame timestamps, never by the wall clock.
/// </summary>
public sealed class IssueDebounce
{
    private DateTime? _presentSince;
    private DateTime? _absentSince;

    public IssueDebounce(CameraIssue issue, TimeSpan raiseAfter, TimeSpan clearAfter)
    {
        Issue = issue;
        RaiseAfter = raiseAfter;
        ClearAfter = clearAfter;
    }

    public CameraIssue Issue { get; }
    public TimeSpan RaiseAfter { get; }
    public TimeSpan ClearAfter { get; }
    public bool Active { get; private set; }

    /// <summary>Feeds one frame's verdict; returns the transition it caused, if any (one per episode).</summary>
    /// <param name="detail">Shown with a raise (the event payload's detail).</param>
    public CameraTransition? Step(bool condition, DateTime at, string detail)
    {
        if (condition)
        {
            _absentSince = null;
            _presentSince ??= at;
            if (Active || at - _presentSince.Value < RaiseAfter) return null;
            Active = true;
            return new CameraTransition(Issue, true, at, detail);
        }

        _presentSince = null;
        if (!Active) return null;
        _absentSince ??= at;
        if (at - _absentSince.Value < ClearAfter) return null;
        Active = false;
        _absentSince = null;
        return new CameraTransition(Issue, false, at, "Resolved");
    }

    /// <summary>
    /// A frame that says nothing about this issue (no picture, model not run): the pending "since" times restart, so
    /// an issue is never raised or cleared across a gap in the evidence, and an active issue stays active.
    /// </summary>
    public void NoEvidence()
    {
        _presentSince = null;
        _absentSince = null;
    }
}
