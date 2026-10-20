using FluentAssertions;
using Student_Desktop_App.Core.Backup;
using Student_Desktop_App.Core.Submission;

namespace Student_Desktop_App.Tests.Submission;

/// <summary>
/// M6-U-07 (TEST_CASES.md §M6, the logic of M6-D-01 "crash recovery"): on relaunch the server's answers and
/// the local backup are merged per question — higher revision wins, unconfirmed local edits are replayed.
/// </summary>
public class AnswerMergeTests
{
    private readonly Guid _q1 = Guid.NewGuid();
    private readonly Guid _q2 = Guid.NewGuid();
    private readonly Guid _q3 = Guid.NewGuid();
    private readonly Guid _q4 = Guid.NewGuid();

    [Fact]
    public void M6_U_07_HigherRevisionWins_UnsentLocalEditsAreReplayed()
    {
        var server = new[]
        {
            new ServerAnswer(_q1, "server v2", 2),      // backup is behind → server
            new ServerAnswer(_q2, "server v1", 1),      // backup has v4 the server never got → local, replay
            new ServerAnswer(_q3, "legacy", 0)          // only on the server (e.g. saved from the web) → kept
        };
        var local = new[]
        {
            new BackupAnswer(_q1, "local v1", 1, 1),
            new BackupAnswer(_q2, "local v4", 4, 1),
            new BackupAnswer(_q4, "never sent", 2, 0)   // only on this PC → replay
        };

        var merged = AnswerMerge.Merge(server, local).ToDictionary(m => m.QuestionId);

        merged[_q1].Should().Be(new MergedAnswer(_q1, "server v2", 2, 2, true, false));
        merged[_q2].Should().Be(new MergedAnswer(_q2, "local v4", 4, 1, true, true));
        merged[_q3].Should().Be(new MergedAnswer(_q3, "legacy", 0, 0, true, false));
        merged[_q4].Should().Be(new MergedAnswer(_q4, "never sent", 2, 0, false, true));
    }

    [Fact]
    public void EqualRevisions_TheServerTextWins_AndNothingIsReplayed()
    {
        var merged = AnswerMerge.Merge(
            new[] { new ServerAnswer(_q1, "server", 3) },
            new[] { new BackupAnswer(_q1, "local", 3, 3) }).Single();

        merged.Text.Should().Be("server");
        merged.NeedsSync.Should().BeFalse();
        merged.Revision.Should().Be(3);
    }

    [Fact]
    public void ServerUnreachable_OrNoBackup_StillProducesAUsableState()
    {
        // Offline at relaunch: every backed-up answer is replayed (the server answers an idempotent 200 for ones it has).
        var offline = AnswerMerge.Merge(null, new[] { new BackupAnswer(_q1, "a", 2, 2) }).Single();
        offline.NeedsSync.Should().BeTrue();
        offline.Text.Should().Be("a");

        // Fresh PC (no backup): the server copy.
        AnswerMerge.Merge(new[] { new ServerAnswer(_q1, "s", 5) }, null).Single()
            .Should().Be(new MergedAnswer(_q1, "s", 5, 5, true, false));

        AnswerMerge.Merge(null, null).Should().BeEmpty();
    }
}
