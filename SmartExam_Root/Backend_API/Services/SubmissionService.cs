using Backend_API.Data;
using Backend_API.Hubs;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Services;

/// <summary>The receipt fixed when a session is finalised (Module 6). Returned flat by submit.</summary>
public record SubmissionReceipt(Guid SessionId, string Status, DateTime SubmittedAt, int AnswerCount, string Sha256);

/// <summary>
/// Single place that finalises an exam session: locks it, fixes the submission receipt, grades the
/// answers and runs the plagiarism check, then tells the live monitor (SessionFinalized). Used by
/// student submit, teacher force-submit-all, the hub's per-student force-submit and the timeout
/// sweeper (SessionTimeoutService) so every path produces results and a receipt.
/// </summary>
public class SubmissionService
{
    // Absorbs network latency / clock drift for the student's last auto-save.
    public static readonly TimeSpan DeadlineGrace = TimeSpan.FromMinutes(2);

    private readonly AppDbContext _db;
    private readonly IHubContext<MonitoringHub> _hub;

    public SubmissionService(AppDbContext db, IHubContext<MonitoringHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    public static DateTime ExamEnd(Exam exam) => exam.StartTime.AddMinutes(exam.DurationMinutes);

    /// One student's deadline: the exam end plus the extra minutes a proctor granted that session.
    /// Caller must have loaded session.Exam.
    public static DateTime EffectiveEnd(ExamSession session) => ExamEnd(session.Exam).AddMinutes(session.ExtraMinutes);

    /// True when the exam no longer accepts answers from anyone (ended by a teacher, or past end time + grace).
    /// Exam-level: used where no session exists yet (start, practice list). Per-student checks use the overload below.
    public static bool IsClosed(Exam exam, DateTime utcNow) =>
        exam.Status == ExamStatus.Ended || utcNow > ExamEnd(exam) + DeadlineGrace;

    /// True when this session no longer accepts answers: the exam was ended (force-submit-all ends it for
    /// everyone, extra time included) or the student's own deadline + grace has passed.
    public static bool IsClosed(ExamSession session, DateTime utcNow) =>
        session.Exam.Status == ExamStatus.Ended || utcNow > EffectiveEnd(session) + DeadlineGrace;

    /// Seconds left on this student's clock (0 once the exam was ended or the deadline passed).
    public static int RemainingSeconds(ExamSession session, DateTime utcNow) =>
        session.Exam.Status == ExamStatus.Ended ? 0 : Math.Max(0, (int)(EffectiveEnd(session) - utcNow).TotalSeconds);

    /// Marks the session submitted with the given status, fixes its receipt, then grades and checks
    /// plagiarism. Caller must have loaded session.Exam. Saves changes; a concurrent finalisation of the
    /// same session (Status is a concurrency token) surfaces as <see cref="DbUpdateConcurrencyException"/>.
    public async Task<SubmissionReceipt> FinalizeAsync(ExamSession session, SessionStatus finalStatus)
    {
        session.Status = finalStatus;
        session.SubmittedAt = DateTime.UtcNow;

        var answers = await _db.Answers
            .Include(a => a.Question)
            .Where(a => a.SessionId == session.SessionId)
            .ToListAsync();

        // The receipt covers the answers exactly as stored at the moment the session locks.
        session.ReceiptSha256 = ReceiptHasher.Compute(session.SessionId, answers.Select(a => (a.QuestionId, (string?)a.AnswerText)));
        session.ReceiptAnswerCount = answers.Count;

        if (session.Exam.AiEvaluationEnabled)
            await GradeAsync(answers);

        // Practice exams: shared answers are expected, not misconduct
        if (!session.Exam.IsMock)
            await DetectPlagiarismAsync(session, answers);

        await _db.SaveChangesAsync();

        var receipt = new SubmissionReceipt(session.SessionId, finalStatus.ToString(), session.SubmittedAt.Value,
            answers.Count, session.ReceiptSha256);

        // Live monitor: the tile flips to Submitted / Force submitted / Timed out (M6-E-03).
        await _hub.Clients.Group(ExamGroupTracker.GroupName(session.ExamId)).SendAsync("SessionFinalized", new
        {
            examId = session.ExamId,
            sessionId = session.SessionId,
            userId = session.UserId,
            status = receipt.Status,
            submittedAt = receipt.SubmittedAt,
            answerCount = receipt.AnswerCount,
            receiptSha256 = receipt.Sha256
        });

        return receipt;
    }

    private async Task GradeAsync(List<Answer> answers)
    {
        foreach (var answer in answers)
        {
            if (await _db.AiGradingResults.AnyAsync(g => g.AnswerId == answer.AnswerId)) continue;

            double suggestedMarks;
            string confidence;
            string justification;

            if (answer.Question.Type == QuestionType.Mcq)
            {
                // Deterministic MCQ grading: answer stores the selected option index
                var correct = int.TryParse(answer.AnswerText?.Trim(), out var selected)
                              && selected == answer.Question.CorrectOptionIndex;
                suggestedMarks = correct ? answer.Question.Marks : 0;
                confidence = "High";
                justification = correct
                    ? "Selected option matches the correct answer."
                    : "Selected option does not match the correct answer.";
            }
            else
            {
                // FYP-1 placeholder: keyword heuristic only. Code is NOT compiled or run against
                // test cases yet, so the suggestion is low-confidence and needs teacher review.
                var answerText = (answer.AnswerText ?? string.Empty).ToLower();
                var keywords = answer.Question.Type == QuestionType.Coding
                    ? new[] { "for", "while", "max", "arr", "return" }
                    : new[] { "lifo", "fifo", "stack", "queue" };
                var hits = keywords.Count(answerText.Contains);
                var ratio = string.IsNullOrWhiteSpace(answerText) ? 0 : hits >= 2 ? 0.9 : hits == 1 ? 0.6 : 0.4;

                suggestedMarks = answer.Question.Marks * ratio;
                confidence = "Low";
                justification = $"Heuristic keyword match ({hits}/{keywords.Length} expected keywords found). " +
                                "Not compiled or test-case verified — teacher review required.";
            }

            _db.AiGradingResults.Add(new AiGradingResult
            {
                AnswerId = answer.AnswerId,
                SuggestedMarks = suggestedMarks,
                Confidence = confidence,
                Justification = justification
            });
        }
    }

    private async Task DetectPlagiarismAsync(ExamSession session, List<Answer> answers)
    {
        foreach (var answer in answers)
        {
            // MCQ answers are just option indexes — identical picks are expected, not plagiarism
            if (answer.Question.Type == QuestionType.Mcq) continue;

            var otherAnswers = await _db.Answers
                .Include(a => a.ExamSession)
                .Where(a => a.QuestionId == answer.QuestionId &&
                            a.ExamSession.ExamId == session.ExamId &&
                            a.ExamSession.UserId != session.UserId &&
                            // Every finalised session counts — TimedOut (Module 6 sweeper) included, or a
                            // student who let the clock run out would never be compared with anyone.
                            a.ExamSession.Status != SessionStatus.InProgress)
                .ToListAsync();

            foreach (var other in otherAnswers)
            {
                var similarity = GetSimilarity(answer.AnswerText, other.AnswerText);
                if (similarity < session.Exam.PlagiarismThreshold) continue;

                var exists = await _db.PlagiarismResults.AnyAsync(pr =>
                    pr.QuestionId == answer.QuestionId &&
                    ((pr.UserIdA == session.UserId && pr.UserIdB == other.ExamSession.UserId) ||
                     (pr.UserIdA == other.ExamSession.UserId && pr.UserIdB == session.UserId)));
                if (exists) continue;

                _db.PlagiarismResults.Add(new PlagiarismResult
                {
                    ExamId = session.ExamId,
                    QuestionId = answer.QuestionId,
                    UserIdA = session.UserId,
                    UserIdB = other.ExamSession.UserId,
                    SimilarityScore = Math.Round(similarity, 1),
                    MatchingSegments = System.Text.Json.JsonSerializer.Serialize(new[]
                    {
                        new { A = answer.AnswerText, B = other.AnswerText }
                    })
                });
            }
        }
    }

    // Jaccard similarity over word sets, as a percentage.
    private static double GetSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0;

        var separators = new[] { ' ', '\n', '\r', '\t', '{', '}', '(', ')', ';', ',', '[', ']' };
        var words1 = s1.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                       .Select(w => w.Trim().ToLower())
                       .ToHashSet();

        var words2 = s2.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                       .Select(w => w.Trim().ToLower())
                       .ToHashSet();

        var intersect = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return union == 0 ? 0 : ((double)intersect / union) * 100;
    }
}
