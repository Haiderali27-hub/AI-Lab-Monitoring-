namespace Student_Desktop_App.Core.Submission;

/// <summary>
/// Module 6 — decides WHEN an edited answer is auto-saved (no timers, no HTTP: the exam window ticks it
/// once a second and sends what it returns). An answer becomes due <see cref="Interval"/> (10 s) after
/// its first unsaved edit, so a student typing continuously is saved about every 10 s and an untouched
/// exam sends nothing at all (dirty flag). Question change, focus loss and submit flush immediately
/// through <see cref="TakeAll"/>. The clock is injected so the rules are unit-tested (M6-U-01, M6-U-02).
/// </summary>
public sealed class AutosaveScheduler
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(10);

    private readonly Func<DateTime> _clock;
    private readonly Dictionary<Guid, DateTime> _dirtySince = new();

    public AutosaveScheduler(Func<DateTime> utcClock, TimeSpan? interval = null)
    {
        _clock = utcClock;
        Interval = interval ?? DefaultInterval;
        if (Interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
    }

    public TimeSpan Interval { get; }

    /// <summary>Answers edited since their last save.</summary>
    public int DirtyCount => _dirtySince.Count;

    public bool IsDirty(Guid questionId) => _dirtySince.ContainsKey(questionId);

    /// <summary>Records an edit. The first unsaved edit starts the question's 10 s window; later ones do not extend it.</summary>
    public void MarkDirty(Guid questionId) => _dirtySince.TryAdd(questionId, _clock());

    /// <summary>The answer was saved by another path (question change, submit): no autosave needed for it.</summary>
    public void MarkClean(Guid questionId) => _dirtySince.Remove(questionId);

    /// <summary>Questions whose first unsaved edit is at least <see cref="Interval"/> old. They are no longer dirty
    /// afterwards; an edit made while the save is in flight marks the question dirty again.</summary>
    public IReadOnlyList<Guid> TakeDue()
    {
        var now = _clock();
        var due = _dirtySince.Where(kv => now - kv.Value >= Interval)
            .OrderBy(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var id in due) _dirtySince.Remove(id);
        return due;
    }

    /// <summary>Every dirty question, oldest edit first (flush on question change / focus loss / submit).</summary>
    public IReadOnlyList<Guid> TakeAll()
    {
        var all = _dirtySince.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList();
        _dirtySince.Clear();
        return all;
    }
}
