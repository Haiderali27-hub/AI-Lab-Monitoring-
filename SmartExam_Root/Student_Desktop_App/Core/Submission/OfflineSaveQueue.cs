namespace Student_Desktop_App.Core.Submission;

/// <summary>One answer save waiting for the network: the text at a given local revision.</summary>
public sealed record PendingSave(Guid QuestionId, string Text, int Revision, DateTime ClientSavedAtUtc);

/// <summary>What became of one save-answer call.</summary>
public enum SaveOutcome
{
    /// <summary>2xx: the server stored this revision (or already had it — idempotent replay).</summary>
    Accepted,
    /// <summary>409 STALE_REVISION: the server already holds a newer revision. Counts as delivered.</summary>
    Stale,
    /// <summary>No answer from the server (network down, timeout, 5xx, 429): keep it and retry.</summary>
    Offline,
    /// <summary>The server refused it for good (time is over, session finished, unknown question): drop it.</summary>
    Rejected
}

public sealed record FlushResult(int Delivered, int Rejected, int Remaining, bool WentOffline);

/// <summary>
/// Module 6 — answer saves that could not reach the server, replayed in order once it is reachable
/// again. Only the newest revision of an answer matters (the server would refuse an older one with
/// STALE_REVISION anyway), so enqueuing a question that is already waiting replaces its entry and moves
/// it to the back, keeping the order of the student's latest edits. Durability across a crash comes
/// from the encrypted local backup, which records which revision the server last confirmed; on
/// relaunch every unconfirmed answer is queued again (<see cref="AnswerMerge"/>). Pure: the sender is injected.
/// </summary>
public sealed class OfflineSaveQueue
{
    private readonly List<PendingSave> _items = new();
    private bool _flushing;

    public int Count => _items.Count;

    public IReadOnlyList<PendingSave> Snapshot() => _items.ToList();

    public bool Contains(Guid questionId) => _items.Any(i => i.QuestionId == questionId);

    /// <summary>Queues a save. Ignored when a newer revision of the same answer is already waiting.</summary>
    public void Enqueue(PendingSave save)
    {
        var existing = _items.FindIndex(i => i.QuestionId == save.QuestionId);
        if (existing >= 0)
        {
            if (_items[existing].Revision > save.Revision) return;
            _items.RemoveAt(existing);
        }
        _items.Add(save);
    }

    /// <summary>
    /// Sends the queue front to back. Delivered (Accepted/Stale) and Rejected entries leave the queue; the
    /// first Offline outcome stops the replay so later edits never overtake earlier ones. A call made while a
    /// flush is already running returns at once (the running one picks up anything queued meanwhile).
    /// </summary>
    public async Task<FlushResult> FlushAsync(Func<PendingSave, Task<SaveOutcome>> send, CancellationToken cancellationToken = default)
    {
        if (_flushing) return new FlushResult(0, 0, _items.Count, false);
        _flushing = true;
        var delivered = 0;
        var rejected = 0;
        try
        {
            while (_items.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var head = _items[0];
                var outcome = await send(head);
                if (outcome == SaveOutcome.Offline)
                    return new FlushResult(delivered, rejected, _items.Count, true);

                // Enqueue may have replaced the entry with a newer revision while it was in flight: that one stays.
                _items.Remove(head);
                if (outcome == SaveOutcome.Rejected) rejected++;
                else delivered++;
            }
            return new FlushResult(delivered, rejected, 0, false);
        }
        finally
        {
            _flushing = false;
        }
    }

    /// <summary>
    /// Maps a save-answer response onto an outcome. <paramref name="statusCode"/> is null when no response
    /// arrived at all. A 401 is treated as Offline: the client refreshes tokens itself and a revoked session
    /// is detected (and ends the exam) by the heartbeat, so an answer is never thrown away on an auth hiccup.
    /// </summary>
    public static SaveOutcome Classify(int? statusCode, string? errorCode)
    {
        if (statusCode is null) return SaveOutcome.Offline;
        var code = statusCode.Value;
        if (code is >= 200 and < 300) return SaveOutcome.Accepted;
        if (code == 409 && string.Equals(errorCode, "STALE_REVISION", StringComparison.Ordinal)) return SaveOutcome.Stale;
        if (code is 401 or 408 or 429 || code >= 500) return SaveOutcome.Offline;
        return SaveOutcome.Rejected;
    }
}
