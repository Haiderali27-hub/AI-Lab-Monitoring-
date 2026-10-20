using System.Text.Json;
using Backend_API.Data;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Backend_API.Services.Grading;

public sealed record MatrixStudent(Guid UserId, string Name, DateTime? SubmittedAt);
public sealed record MatrixQuestion(Guid QuestionId, string Type, int OrderIndex, string BodyText);
public sealed record PlagiarismMatrix(IReadOnlyList<MatrixStudent> Students, IReadOnlyList<MatrixQuestion> Questions, double[][] Matrix);

/// <summary>
/// Module 11 — plagiarism over an exam's answers with <see cref="PlagiarismEngine"/>:
/// <list type="bullet">
/// <item><see cref="DetectAsync"/> runs when a session is finalised and stores a <see cref="PlagiarismResult"/> for every
///       pair at or above the exam's threshold (with matched line ranges), as before.</item>
/// <item><see cref="BuildMatrixAsync"/> computes the class-wide pairwise matrix on demand from all finalised sessions
///       (stored rows only exist above the threshold, so they cannot feed a heat map).</item>
/// </list>
/// Fingerprints are cached per answer: a finalised answer never changes.
/// </summary>
public sealed class PlagiarismService(AppDbContext db, PlagiarismFingerprintCache cache)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly SessionStatus[] Finalised = [SessionStatus.Submitted, SessionStatus.ForceSubmitted, SessionStatus.TimedOut];

    /// <summary>Compares the session's answers with every other finalised session of the exam (caller saves).</summary>
    public async Task DetectAsync(ExamSession session, IReadOnlyList<Answer> answers)
    {
        foreach (var answer in answers)
        {
            // MCQ answers are option indexes: identical picks are expected, not plagiarism.
            if (answer.Question.Type == QuestionType.Mcq || string.IsNullOrWhiteSpace(answer.AnswerText)) continue;
            var isCode = answer.Question.Type == QuestionType.Coding;

            var others = await db.Answers
                .Include(a => a.ExamSession)
                .Where(a => a.QuestionId == answer.QuestionId &&
                            a.ExamSession.ExamId == session.ExamId &&
                            a.ExamSession.UserId != session.UserId &&
                            // Every finalised session counts — TimedOut (Module 6 sweeper) included, or a
                            // student who let the clock run out would never be compared with anyone.
                            a.ExamSession.Status != SessionStatus.InProgress)
                .ToListAsync();

            var mine = Fingerprint(answer, isCode);
            foreach (var other in others)
            {
                if (string.IsNullOrWhiteSpace(other.AnswerText)) continue;
                var comparison = PlagiarismEngine.Compare(mine, Fingerprint(other, isCode));
                if (comparison.Similarity < session.Exam.PlagiarismThreshold) continue;

                var exists = await db.PlagiarismResults.AnyAsync(pr =>
                    pr.QuestionId == answer.QuestionId &&
                    ((pr.UserIdA == session.UserId && pr.UserIdB == other.ExamSession.UserId) ||
                     (pr.UserIdA == other.ExamSession.UserId && pr.UserIdB == session.UserId)));
                if (exists) continue;

                db.PlagiarismResults.Add(new PlagiarismResult
                {
                    ExamId = session.ExamId,
                    QuestionId = answer.QuestionId,
                    UserIdA = session.UserId,
                    UserIdB = other.ExamSession.UserId,
                    SimilarityScore = Math.Round(comparison.Similarity, 1),
                    // [{ aStartLine, aEndLine, bStartLine, bEndLine, A, B }] — A/B are the matched excerpts.
                    MatchingSegments = JsonSerializer.Serialize(comparison.Segments, Json)
                });
            }
        }
    }

    /// <summary>
    /// Symmetric students × students similarity (diagonal 100) over the finalised sessions, students in submission order.
    /// With <paramref name="questionId"/> one question, otherwise each pair's highest similarity over the non-MCQ questions.
    /// </summary>
    public async Task<PlagiarismMatrix> BuildMatrixAsync(Guid examId, Guid? questionId)
    {
        var questions = await db.Questions.AsNoTracking()
            .Where(q => q.ExamId == examId && q.Type != QuestionType.Mcq && (questionId == null || q.QuestionId == questionId))
            .OrderBy(q => q.OrderIndex)
            .Select(q => new MatrixQuestion(q.QuestionId, q.Type.ToString(), q.OrderIndex, q.BodyText))
            .ToListAsync();

        var sessions = await db.ExamSessions.AsNoTracking()
            .Where(s => s.ExamId == examId && Finalised.Contains(s.Status))
            .Include(s => s.Student)
            .ToListAsync();
        // One row per student (their latest finalised attempt), ordered by when they submitted.
        var latest = sessions
            .GroupBy(s => s.UserId)
            .Select(g => g.OrderByDescending(s => s.SubmittedAt ?? s.StartedAt).First())
            .OrderBy(s => s.SubmittedAt ?? DateTime.MaxValue).ThenBy(s => s.Student.Name)
            .ToList();

        var sessionIds = latest.Select(s => s.SessionId).ToList();
        var questionIds = questions.Select(q => q.QuestionId).ToList();
        var answers = await db.Answers.AsNoTracking()
            .Where(a => sessionIds.Contains(a.SessionId) && questionIds.Contains(a.QuestionId))
            .ToListAsync();
        var bySession = answers.ToLookup(a => a.SessionId);
        var types = questions.ToDictionary(q => q.QuestionId, q => q.Type == nameof(QuestionType.Coding));

        var n = latest.Count;
        var matrix = new double[n][];
        for (var i = 0; i < n; i++)
        {
            matrix[i] = new double[n];
            matrix[i][i] = 100;
        }

        for (var i = 0; i < n; i++)
        {
            var mine = bySession[latest[i].SessionId].ToDictionary(a => a.QuestionId);
            for (var j = i + 1; j < n; j++)
            {
                var best = 0.0;
                foreach (var other in bySession[latest[j].SessionId])
                {
                    if (!mine.TryGetValue(other.QuestionId, out var answer)) continue;
                    if (string.IsNullOrWhiteSpace(answer.AnswerText) || string.IsNullOrWhiteSpace(other.AnswerText)) continue;
                    var isCode = types[other.QuestionId];
                    best = Math.Max(best, PlagiarismEngine.Compare(Fingerprint(answer, isCode), Fingerprint(other, isCode)).Similarity);
                }
                matrix[i][j] = matrix[j][i] = Math.Round(best, 1);
            }
        }

        return new PlagiarismMatrix(
            latest.Select(s => new MatrixStudent(s.UserId, s.Student.Name, s.SubmittedAt)).ToList(),
            questions,
            matrix);
    }

    private AnswerFingerprint Fingerprint(Answer answer, bool isCode) => cache.Get(answer, isCode);
}

/// <summary>
/// Bounded in-memory cache of answer fingerprints (singleton). Keyed by the answer's revision and save time as well,
/// so an answer still being edited is never served stale.
/// </summary>
public sealed class PlagiarismFingerprintCache : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 20_000 });

    public AnswerFingerprint Get(Answer answer, bool isCode) =>
        _cache.GetOrCreate((answer.AnswerId, answer.Revision, answer.LastSavedAt, isCode), entry =>
        {
            entry.SetSlidingExpiration(TimeSpan.FromHours(2));
            entry.SetSize(1);
            return PlagiarismEngine.Fingerprint(answer.AnswerText, isCode);
        })!;

    public void Dispose() => _cache.Dispose();
}
