namespace Student_Desktop_App.Core.Lockdown;

/// <summary>What the exam window should do with its lockdown after a heartbeat ack.</summary>
public enum SuspendAction
{
    /// <summary>Nothing changes.</summary>
    None,
    /// <summary>A proctor unlocked the PC: stop the guards until the deadline.</summary>
    Suspend,
    /// <summary>Still unlocked, but the deadline moved (a new unlock, or the server's clock corrected it).</summary>
    Reschedule,
    /// <summary>The unlock ended (expired, or a proctor relocked early): turn the guards back on.</summary>
    Resume
}

/// <summary>
/// Module 10 — manual unlock on the desktop side. The server sends the unlock deadline (<c>unlockedUntilUtc</c>) in
/// every heartbeat ack, in SERVER time. This converts it to this PC's clock using the ack's own server time (a PC
/// whose clock is minutes off still re-locks after exactly the granted minutes) and decides whether the guards must
/// be suspended, rescheduled or resumed. The window also re-locks on its own timer when the local deadline passes,
/// so a network outage during the unlock never leaves the PC open. Pure: clocks are passed in.
/// </summary>
public static class LockdownSuspendPolicy
{
    /// <summary>Never trust an ack for longer than the hub allows (MonitoringHub.MaxUnlockMinutes).</summary>
    public static readonly TimeSpan MaxUnlock = TimeSpan.FromMinutes(30);

    /// <summary>Deadline changes smaller than this (heartbeat latency) do not reschedule.</summary>
    public static readonly TimeSpan RescheduleTolerance = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The unlock deadline on this PC's clock, or null when the PC is not (or no longer) unlocked.
    /// </summary>
    /// <param name="serverUntilUtc">unlockedUntilUtc from the ack (server clock).</param>
    /// <param name="serverNowUtc">receivedAtUtc from the same ack (server clock).</param>
    /// <param name="localNowUtc">This PC's UTC clock when the ack arrived.</param>
    public static DateTime? LocalDeadline(DateTime? serverUntilUtc, DateTime serverNowUtc, DateTime localNowUtc)
    {
        if (serverUntilUtc is not DateTime until) return null;
        var remaining = ToUtc(until) - ToUtc(serverNowUtc);
        if (remaining <= TimeSpan.Zero) return null;
        if (remaining > MaxUnlock) remaining = MaxUnlock;
        return ToUtc(localNowUtc) + remaining;
    }

    /// <param name="currentDeadline">The local deadline the window is suspended until, or null when locked.</param>
    /// <param name="newDeadline">From <see cref="LocalDeadline"/> for the latest ack.</param>
    /// <param name="localNowUtc">This PC's UTC clock.</param>
    public static SuspendAction Decide(DateTime? currentDeadline, DateTime? newDeadline, DateTime localNowUtc)
    {
        var suspended = currentDeadline is DateTime current && current > localNowUtc;
        var unlocked = newDeadline is DateTime next && next > localNowUtc;

        if (!unlocked) return suspended || currentDeadline is not null ? SuspendAction.Resume : SuspendAction.None;
        if (!suspended) return SuspendAction.Suspend;
        return (newDeadline!.Value - currentDeadline!.Value).Duration() > RescheduleTolerance
            ? SuspendAction.Reschedule
            : SuspendAction.None;
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
