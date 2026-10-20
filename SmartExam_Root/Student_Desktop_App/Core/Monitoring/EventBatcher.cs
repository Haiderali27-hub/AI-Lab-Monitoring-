namespace Student_Desktop_App.Core.Monitoring;

/// <summary>
/// Module 5 — decides WHEN queued monitoring events are sent (thesis Ch-3-6: batched every 5 s). The monitoring
/// service ticks it about once a second: a flush is due when events are waiting and <see cref="Interval"/> has
/// passed since the last flush, or at once when a violation is waiting (the proctor should see it live). After a
/// failed flush the wait doubles up to <see cref="MaxBackoff"/>, so an offline PC does not hammer the network; a
/// successful heartbeat (<see cref="NetworkRestored"/>) ends the backoff so the backlog is replayed straight away.
/// Pure: the clock is injected (M5-U-06).
/// </summary>
public sealed class EventBatcher
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    private readonly Func<DateTime> _clock;
    private DateTime _lastAttempt;
    private TimeSpan _wait;
    private bool _failing;

    public EventBatcher(Func<DateTime> utcClock, TimeSpan? interval = null)
    {
        _clock = utcClock;
        Interval = interval ?? DefaultInterval;
        if (Interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        _lastAttempt = _clock();
        _wait = Interval;
    }

    public TimeSpan Interval { get; }

    /// <summary>Current wait between attempts (grows while flushes fail).</summary>
    public TimeSpan CurrentWait => _wait;

    /// <summary>True when a flush should be attempted now.</summary>
    /// <param name="pending">Events waiting in the queue.</param>
    /// <param name="urgentPending">A violation is among them (sent without waiting for the 5 s boundary, unless offline).</param>
    public bool IsDue(int pending, bool urgentPending)
    {
        if (pending <= 0) return false;
        if (urgentPending && !_failing) return true;
        return _clock() - _lastAttempt >= _wait;
    }

    /// <summary>The flush delivered everything it sent.</summary>
    public void MarkFlushed()
    {
        _lastAttempt = _clock();
        _wait = Interval;
        _failing = false;
    }

    /// <summary>The flush could not reach the server: back off (5 s, 10 s, 20 s … 60 s).</summary>
    public void MarkFailed()
    {
        _lastAttempt = _clock();
        _wait = _failing ? TimeSpan.FromTicks(Math.Min(_wait.Ticks * 2, MaxBackoff.Ticks)) : Interval;
        _failing = true;
    }

    /// <summary>The server answered something else (a heartbeat): replay the backlog at the next tick.</summary>
    public void NetworkRestored()
    {
        if (!_failing) return;
        _failing = false;
        _wait = TimeSpan.Zero;
    }
}
