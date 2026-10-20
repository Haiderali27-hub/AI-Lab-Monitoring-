using Backend_API.Data;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Services;

/// <summary>
/// Single place that finalises an exam session: locks it, grades the answers and runs the
/// plagiarism check. Used by student submit, teacher force-submit-all and the hub's
/// per-student force-submit so every path produces results.
/// </summary>
public class SubmissionService
{
    // Absorbs network latency / clock drift for the student's last auto-save.
    public static readonly TimeSpan DeadlineGrace = TimeSpan.FromMinutes(2);

    private readonly AppDbContext _db;

    public SubmissionService(AppDbContext db)
    {
        _db = db;
    }

    public static DateTime ExamEnd(Exam exam) => exam.StartTime.AddMinutes(exam.DurationMinutes);

    /// True when the exam no longer accepts answers (ended by a teacher, or past end time + grace).
    public static bool IsClosed(Exam exam, DateTime utcNow) =>
        exam.Status == ExamStatus.Ended || utcNow > ExamEnd(exam) + DeadlineGrace;

    /// Marks the session submitted with the given status, then grades and checks plagiarism.
    /// Caller must have loaded session.Exam. Saves changes.
    public async Task FinalizeAsync(ExamSession session, SessionStatus finalStatus)
    {
        session.Status = finalStatus;
        session.SubmittedAt = DateTime.UtcNow;

        var answers = await _db.Answers
            .Include(a => a.Question)
            .Where(a => a.SessionId == session.SessionId)
            .ToListAsync();

        if (session.Exam.AiEvaluationEnabled)
            await GradeAsync(answers);

        await DetectPlagiarismAsync(session, answers);

        await _db.SaveChangesAsync();
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
                            (a.ExamSession.Status == SessionStatus.Submitted ||
                             a.ExamSession.Status == SessionStatus.ForceSubmitted))
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
