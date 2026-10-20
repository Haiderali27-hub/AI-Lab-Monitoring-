namespace Student_Desktop_App.Core.Monitoring;

/// <summary>
/// Module 5 — when to take screen captures (and webcam snapshots), and how big to make them. Pure: the clock and
/// the random source are injected (M5-U-05).
/// <list type="bullet">
/// <item>Interval captures follow a random schedule: each wait is the exam's mean interval
/// (<c>Exam.ScreenshotIntervalSeconds</c>, default 60) times a uniform factor in [0.5, 1.5], so the default gives
/// the thesis' random 30–90 s and a student cannot time anything around a fixed beat.</item>
/// <item>A violation triggers an immediate capture, at most one per <see cref="ViolationCooldown"/> (a student
/// hammering Alt+Tab must not flood the server).</item>
/// <item><see cref="TargetSize"/> scales the image so its longest side is at most 1280 px (aspect kept).</item>
/// </list>
/// </summary>
public sealed class CaptureSchedule
{
    public const int MaxSide = 1280;
    public const int JpegQuality = 60;
    public const int MinMeanSeconds = 10;
    public const int MaxMeanSeconds = 600;
    public static readonly TimeSpan ViolationCooldown = TimeSpan.FromSeconds(15);

    private readonly Func<DateTime> _clock;
    private readonly Func<double> _random;
    private DateTime _lastViolationCapture = DateTime.MinValue;
    private int _meanSeconds;

    /// <param name="random">Uniform [0, 1); production passes <see cref="Random.Shared"/>.</param>
    public CaptureSchedule(int meanSeconds, Func<DateTime> utcClock, Func<double> random)
    {
        _clock = utcClock;
        _random = random;
        _meanSeconds = ClampMean(meanSeconds);
        NextDueUtc = _clock() + NextInterval();
    }

    /// <summary>When the next interval capture is due.</summary>
    public DateTime NextDueUtc { get; private set; }

    public int MeanSeconds => _meanSeconds;

    /// <summary>The teacher changed the interval mid-exam (heartbeat ack): reschedule if the new one is sooner.</summary>
    public void UpdateMean(int meanSeconds)
    {
        var clamped = ClampMean(meanSeconds);
        if (clamped == _meanSeconds) return;
        _meanSeconds = clamped;
        var candidate = _clock() + NextInterval();
        if (candidate < NextDueUtc) NextDueUtc = candidate;
    }

    /// <summary>True (and the next capture is scheduled) when an interval capture is due now.</summary>
    public bool TakeIntervalDue()
    {
        var now = _clock();
        if (now < NextDueUtc) return false;
        NextDueUtc = now + NextInterval();
        return true;
    }

    /// <summary>True when a violation capture may be taken now (then the cooldown starts).</summary>
    public bool TryClaimViolationCapture()
    {
        var now = _clock();
        if (now - _lastViolationCapture < ViolationCooldown) return false;
        _lastViolationCapture = now;
        return true;
    }

    /// <summary>One random wait: mean × U[0.5, 1.5).</summary>
    public TimeSpan NextInterval() => TimeSpan.FromSeconds(_meanSeconds * (0.5 + Math.Clamp(_random(), 0, 1)));

    /// <summary>Size to encode a <paramref name="width"/>×<paramref name="height"/> capture at: longest side ≤ <paramref name="maxSide"/>.</summary>
    public static (int Width, int Height) TargetSize(int width, int height, int maxSide = MaxSide)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Capture has no pixels.");
        var longest = Math.Max(width, height);
        if (longest <= maxSide) return (width, height);
        var scale = (double)maxSide / longest;
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static int ClampMean(int seconds) => Math.Clamp(seconds <= 0 ? 60 : seconds, MinMeanSeconds, MaxMeanSeconds);
}
