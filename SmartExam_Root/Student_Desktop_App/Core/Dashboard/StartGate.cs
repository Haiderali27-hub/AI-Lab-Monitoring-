namespace Student_Desktop_App.Core.Dashboard;

/// <summary>The facts from GET /api/exams/student/current that decide whether Start may be pressed.</summary>
public record StartGateInput(bool IsEligible, string Status, DateTime? OpensAtUtc, DateTime? EndUtc, string? SeatStatus = null);

/// <summary>
/// Module 3 — pure rules for the dashboard's Start button (no WPF, no HTTP), unit-tested.
/// Everything is evaluated on the SERVER clock: the offset between the server's
/// <c>serverTimeUtc</c> and the local clock at fetch time is applied to the local clock, so a
/// student cannot unlock Start early by changing the PC's time. The server re-checks on start anyway
/// (EXAM_NOT_STARTED / SEAT_MISMATCH) — this only keeps the UI honest.
/// </summary>
public static class StartGate
{
    /// <summary>serverTimeUtc − local UTC at the moment the status was fetched (zero if the server sent none).</summary>
    public static TimeSpan ClockOffset(DateTime? serverTimeUtc, DateTime localUtcAtFetch) =>
        serverTimeUtc is null ? TimeSpan.Zero : AsUtc(serverTimeUtc.Value) - AsUtc(localUtcAtFetch);

    public static DateTime ServerNow(TimeSpan clockOffset, DateTime localUtcNow) => AsUtc(localUtcNow) + clockOffset;

    /// <summary>When Start unlocks: the server's opensAtUtc, else StartTime − early-join minutes.</summary>
    public static DateTime? OpensAt(DateTime? startUtc, int earlyJoinMinutes, DateTime? opensAtUtc) =>
        opensAtUtc is { } o ? AsUtc(o) : startUtc is { } s ? AsUtc(s).AddMinutes(-Math.Max(0, earlyJoinMinutes)) : null;

    /// <summary>Time left until Start unlocks (zero once open, or when no start time is known).</summary>
    public static TimeSpan UntilOpen(DateTime? opensAtUtc, TimeSpan clockOffset, DateTime localUtcNow)
    {
        if (opensAtUtc is null) return TimeSpan.Zero;
        var left = AsUtc(opensAtUtc.Value) - ServerNow(clockOffset, localUtcNow);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    public static bool HasOpened(DateTime? opensAtUtc, TimeSpan clockOffset, DateTime localUtcNow) =>
        opensAtUtc is not null && UntilOpen(opensAtUtc, clockOffset, localUtcNow) == TimeSpan.Zero;

    /// <summary>
    /// Start is offered when the student is eligible, the window has opened and not ended (server
    /// clock), no session is running or finished, and the server did not report a seat mismatch.
    /// Check-in and the readiness check are layered on top by the view model.
    /// </summary>
    public static bool CanStart(StartGateInput s, TimeSpan clockOffset, DateTime localUtcNow)
    {
        if (!s.IsEligible) return false;
        if (string.Equals(s.SeatStatus, "Mismatch", StringComparison.OrdinalIgnoreCase)) return false;
        if (s.Status is not null && !string.Equals(s.Status, "NotStarted", StringComparison.OrdinalIgnoreCase)) return false;
        if (!HasOpened(s.OpensAtUtc, clockOffset, localUtcNow)) return false;
        return s.EndUtc is { } end && ServerNow(clockOffset, localUtcNow) < AsUtc(end);
    }

    /// <summary>
    /// Module 6 crash recovery: a session that is still <c>InProgress</c> on the server can be resumed (no new
    /// check-in or readiness check — the server returns the same session from student/start) while the
    /// student's own deadline, which includes any extra minutes, is still ahead on the server clock.
    /// </summary>
    public static bool CanResume(StartGateInput s, TimeSpan clockOffset, DateTime localUtcNow)
    {
        if (!s.IsEligible) return false;
        if (string.Equals(s.SeatStatus, "Mismatch", StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(s.Status, "InProgress", StringComparison.OrdinalIgnoreCase)) return false;
        return s.EndUtc is { } end && ServerNow(clockOffset, localUtcNow) < AsUtc(end);
    }

    /// <summary>"hh:mm:ss", or "d.hh:mm:ss" past a day (an exam scheduled next week).</summary>
    public static string Format(TimeSpan t) =>
        t.TotalDays >= 1 ? t.ToString(@"d\.hh\:mm\:ss") : t.ToString(@"hh\:mm\:ss");

    // JSON from the server is UTC; a value parsed without Kind is treated as UTC too.
    private static DateTime AsUtc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc)
    };
}
