using FluentAssertions;
using Student_Desktop_App.Core.Submission;

namespace Student_Desktop_App.Tests.Submission;

/// <summary>M6-U-01 / M6-U-02 (TEST_CASES.md §M6): the 10 s dirty-flag autosave, on a fake clock.</summary>
public class AutosaveSchedulerTests
{
    private DateTime _now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
    private readonly Guid _q1 = Guid.NewGuid();
    private readonly Guid _q2 = Guid.NewGuid();

    private AutosaveScheduler NewScheduler() => new(() => _now);

    // Simulates the exam window's 1 s tick for `seconds` and counts the saves it would send.
    private int RunSeconds(AutosaveScheduler s, int seconds, List<Guid>? saved = null)
    {
        var count = 0;
        for (var i = 0; i < seconds; i++)
        {
            _now = _now.AddSeconds(1);
            var due = s.TakeDue();
            count += due.Count;
            saved?.AddRange(due);
        }
        return count;
    }

    [Fact]
    public void M6_U_01_NoEdits_For30Seconds_SendsNothing()
    {
        var s = NewScheduler();
        RunSeconds(s, 30).Should().Be(0);
        s.DirtyCount.Should().Be(0);
    }

    [Fact]
    public void M6_U_02_OneEdit_IsSavedOnce_TenSecondsLater()
    {
        var s = NewScheduler();
        s.MarkDirty(_q1);

        RunSeconds(s, 9).Should().Be(0, "the answer is not due before 10 s");
        var saved = new List<Guid>();
        RunSeconds(s, 1, saved).Should().Be(1);
        saved.Should().Equal(_q1);
        RunSeconds(s, 30).Should().Be(0, "nothing changed since the save");
    }

    [Fact]
    public void ContinuousTyping_IsSavedAboutEveryTenSeconds_NotOnEveryKeystroke()
    {
        var s = NewScheduler();
        var saves = 0;
        for (var second = 0; second < 30; second++)
        {
            s.MarkDirty(_q1);                // a keystroke every second
            saves += RunSeconds(s, 1);
        }
        saves.Should().Be(3);
    }

    [Fact]
    public void LaterEdits_DoNotPostponeTheFirstEditsDeadline()
    {
        var s = NewScheduler();
        s.MarkDirty(_q1);
        RunSeconds(s, 5);
        s.MarkDirty(_q1);
        RunSeconds(s, 5).Should().Be(1, "10 s after the FIRST unsaved edit");
    }

    [Fact]
    public void MarkClean_CancelsThePendingAutosave_AndTakeAll_FlushesOldestFirst()
    {
        var s = NewScheduler();
        s.MarkDirty(_q1);
        s.MarkClean(_q1);                    // saved on question change
        RunSeconds(s, 15).Should().Be(0);

        s.MarkDirty(_q2);
        _now = _now.AddSeconds(1);
        s.MarkDirty(_q1);
        s.IsDirty(_q1).Should().BeTrue();
        s.TakeAll().Should().Equal(_q2, _q1);
        s.DirtyCount.Should().Be(0);
    }

    [Fact]
    public void Interval_MustBePositive()
    {
        var act = () => new AutosaveScheduler(() => _now, TimeSpan.Zero);
        act.Should().Throw<ArgumentOutOfRangeException>();
        new AutosaveScheduler(() => _now).Interval.Should().Be(TimeSpan.FromSeconds(10));
    }
}
