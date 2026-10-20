using FluentAssertions;
using Student_Desktop_App.Core.Submission;

namespace Student_Desktop_App.Tests.Submission;

/// <summary>
/// M6-U-06 (TEST_CASES.md §M6, part of M6-D-02 "offline answering"): saves made while the network is down
/// are replayed in order, the newest revision of an answer wins, and nothing is dropped unless the server
/// refuses it for good.
/// </summary>
public class OfflineSaveQueueTests
{
    private static readonly DateTime T = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
    private readonly Guid _q1 = Guid.NewGuid();
    private readonly Guid _q2 = Guid.NewGuid();
    private readonly Guid _q3 = Guid.NewGuid();

    private static PendingSave Save(Guid q, string text, int rev) => new(q, text, rev, T);

    [Fact]
    public async Task M6_U_06_ThreeAnswersWhileOffline_AreReplayedInOrder_OnceOnline()
    {
        var queue = new OfflineSaveQueue();
        queue.Enqueue(Save(_q1, "a", 1));
        queue.Enqueue(Save(_q2, "b", 1));
        queue.Enqueue(Save(_q3, "c", 1));

        // Still offline: the first send fails, nothing is lost and nothing overtakes it.
        var attempts = new List<Guid>();
        var offline = await queue.FlushAsync(s => { attempts.Add(s.QuestionId); return Task.FromResult(SaveOutcome.Offline); });
        offline.Should().Be(new FlushResult(0, 0, 3, true));
        attempts.Should().Equal(_q1);

        var sent = new List<Guid>();
        var online = await queue.FlushAsync(s => { sent.Add(s.QuestionId); return Task.FromResult(SaveOutcome.Accepted); });
        online.Should().Be(new FlushResult(3, 0, 0, false));
        sent.Should().Equal(_q1, _q2, _q3);
        queue.Count.Should().Be(0);
    }

    [Fact]
    public void NewerRevision_ReplacesTheWaitingEntry_AndMovesItToTheBack_OlderOneIsIgnored()
    {
        var queue = new OfflineSaveQueue();
        queue.Enqueue(Save(_q1, "draft", 1));
        queue.Enqueue(Save(_q2, "b", 1));
        queue.Enqueue(Save(_q1, "final", 3));
        queue.Enqueue(Save(_q1, "stale", 2));

        queue.Snapshot().Select(s => (s.QuestionId, s.Text, s.Revision))
            .Should().Equal((_q2, "b", 1), (_q1, "final", 3));
        queue.Contains(_q1).Should().BeTrue();
    }

    [Fact]
    public async Task StaleAndRejected_LeaveTheQueue_StopOnlyOnOffline()
    {
        var queue = new OfflineSaveQueue();
        queue.Enqueue(Save(_q1, "a", 1));
        queue.Enqueue(Save(_q2, "b", 1));
        queue.Enqueue(Save(_q3, "c", 1));
        var outcomes = new Queue<SaveOutcome>(new[] { SaveOutcome.Stale, SaveOutcome.Rejected, SaveOutcome.Offline });

        var result = await queue.FlushAsync(_ => Task.FromResult(outcomes.Dequeue()));

        result.Should().Be(new FlushResult(1, 1, 1, true));
        queue.Snapshot().Single().QuestionId.Should().Be(_q3);
    }

    [Fact]
    public async Task AnEditMadeWhileItsOlderSaveIsInFlight_StaysQueued()
    {
        var queue = new OfflineSaveQueue();
        queue.Enqueue(Save(_q1, "v1", 1));
        var calls = 0;

        var result = await queue.FlushAsync(s =>
        {
            calls++;
            if (calls == 1) queue.Enqueue(Save(_q1, "v2", 2));    // student typed during the request
            return Task.FromResult(SaveOutcome.Accepted);
        });

        result.Delivered.Should().Be(2);
        calls.Should().Be(2, "v2 is sent after v1, never dropped");
    }

    [Fact]
    public async Task ConcurrentFlush_ReturnsImmediately()
    {
        var queue = new OfflineSaveQueue();
        queue.Enqueue(Save(_q1, "a", 1));
        var gate = new TaskCompletionSource<SaveOutcome>();

        var first = queue.FlushAsync(_ => gate.Task);
        var second = await queue.FlushAsync(_ => Task.FromResult(SaveOutcome.Accepted));
        second.Should().Be(new FlushResult(0, 0, 1, false));

        gate.SetResult(SaveOutcome.Accepted);
        (await first).Delivered.Should().Be(1);
    }

    [Theory]
    [InlineData(200, null, SaveOutcome.Accepted)]
    [InlineData(409, "STALE_REVISION", SaveOutcome.Stale)]
    [InlineData(409, null, SaveOutcome.Rejected)]
    [InlineData(400, null, SaveOutcome.Rejected)]   // time is over / session finished
    [InlineData(404, null, SaveOutcome.Rejected)]
    [InlineData(401, null, SaveOutcome.Offline)]    // auth hiccup: keep the answer, the heartbeat handles revocation
    [InlineData(408, null, SaveOutcome.Offline)]
    [InlineData(429, null, SaveOutcome.Offline)]
    [InlineData(502, null, SaveOutcome.Offline)]
    [InlineData(null, null, SaveOutcome.Offline)]   // no response at all
    public void Classify_MapsServerResponses(int? status, string? code, SaveOutcome expected) =>
        OfflineSaveQueue.Classify(status, code).Should().Be(expected);
}
