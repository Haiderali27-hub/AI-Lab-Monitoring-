namespace Student_Desktop_App.Core.Monitoring;

/// <summary>A proctor's warning as the heartbeat ack delivers it (Module 10, D-05).</summary>
public sealed record ProctorWarning(Guid NotificationId, string Title, string Body, DateTime CreatedAt);

/// <summary>
/// Module 10 (D-05) — the exam window's inbox for proctor warnings. The server repeats every unread warning in each
/// heartbeat ack until the student acknowledges it, so the same warning arrives every 10 s; this keeps the banner
/// from flashing it again on each beat, keeps the not-yet-acknowledged ones in order (oldest first, the banner shows
/// them one by one) and remembers what was acknowledged even when marking it read on the server failed (offline):
/// the server keeps resending it until the next successful mark-read, but the student is not asked twice.
/// Thread-safe (heartbeats arrive on a timer thread, acknowledgements on the UI thread). Pure, no I/O.
/// </summary>
public sealed class WarningInbox
{
    private readonly object _gate = new();
    private readonly HashSet<Guid> _seen = new();
    private readonly List<ProctorWarning> _pending = new();

    /// <summary>Warnings shown but not yet acknowledged, oldest first.</summary>
    public IReadOnlyList<ProctorWarning> Pending
    {
        get { lock (_gate) return _pending.ToList(); }
    }

    /// <summary>The one the banner shows: the oldest unacknowledged warning, or null.</summary>
    public ProctorWarning? Current
    {
        get { lock (_gate) return _pending.Count == 0 ? null : _pending[0]; }
    }

    /// <summary>
    /// Takes the warnings from one heartbeat ack and returns the ones never seen before (oldest first). Blank or
    /// duplicate entries are ignored.
    /// </summary>
    public IReadOnlyList<ProctorWarning> Accept(IEnumerable<ProctorWarning>? warnings)
    {
        if (warnings is null) return Array.Empty<ProctorWarning>();
        var fresh = new List<ProctorWarning>();
        lock (_gate)
        {
            foreach (var w in warnings.OrderBy(w => w.CreatedAt))
            {
                if (w.NotificationId == Guid.Empty || string.IsNullOrWhiteSpace(w.Body)) continue;
                if (!_seen.Add(w.NotificationId)) continue;
                _pending.Add(w);
                fresh.Add(w);
            }
        }
        return fresh;
    }

    /// <summary>The student acknowledged a warning: it leaves the banner for good. False when it was not pending.</summary>
    public bool Acknowledge(Guid notificationId)
    {
        lock (_gate) return _pending.RemoveAll(w => w.NotificationId == notificationId) > 0;
    }
}
