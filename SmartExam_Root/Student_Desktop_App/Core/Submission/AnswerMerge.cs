using Student_Desktop_App.Core.Backup;

namespace Student_Desktop_App.Core.Submission;

/// <summary>An answer as the server holds it (GET /api/exams/sessions/{id}/answers).</summary>
public sealed record ServerAnswer(Guid QuestionId, string Text, int Revision);

/// <summary>
/// The answer the exam window resumes with. <see cref="NeedsSync"/> = the server does not have this text yet,
/// so it is queued for replay; <see cref="AckedRevision"/> is the revision the server is known to hold.
/// </summary>
public sealed record MergedAnswer(Guid QuestionId, string Text, int Revision, int AckedRevision, bool OnServer, bool NeedsSync);

/// <summary>
/// Module 6 crash recovery — combines the server's answers with the encrypted local backup, per question:
/// the higher revision wins (the backup holds edits the server never received); on equal revisions the
/// server's text wins (it is authoritative and the revision was confirmed); a question only the backup knows
/// is replayed; a question only the server knows is kept as is. Pure and unit-tested.
/// </summary>
public static class AnswerMerge
{
    public static IReadOnlyList<MergedAnswer> Merge(IEnumerable<ServerAnswer>? server, IEnumerable<BackupAnswer>? local)
    {
        var serverById = (server ?? Enumerable.Empty<ServerAnswer>()).GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.Revision).First());
        var localById = (local ?? Enumerable.Empty<BackupAnswer>()).GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.Revision).First());

        var merged = new List<MergedAnswer>();
        foreach (var id in serverById.Keys.Union(localById.Keys))
        {
            serverById.TryGetValue(id, out var s);
            localById.TryGetValue(id, out var l);

            if (s is not null && (l is null || l.Revision <= s.Revision))
            {
                // Server copy is current. Keep counting from the higher of the two revisions.
                merged.Add(new MergedAnswer(id, s.Text, Math.Max(s.Revision, l?.Revision ?? 0), s.Revision, true, false));
            }
            else if (l is not null)
            {
                // Newer local edit (or one the server never saw): resume with it and send it again.
                var acked = s?.Revision ?? 0;
                merged.Add(new MergedAnswer(id, l.Text, l.Revision, acked, s is not null, l.Revision > 0));
            }
        }
        return merged;
    }
}
