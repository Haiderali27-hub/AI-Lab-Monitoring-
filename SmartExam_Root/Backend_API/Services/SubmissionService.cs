using Backend_API.Data;
using Backend_API.Hubs;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Backend_API.Services.Grading;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Services;

/// <summary>The receipt fixed when a session is finalised (Module 6). Returned flat by submit.</summary>
public record SubmissionReceipt(Guid SessionId, string Status, DateTime SubmittedAt, int AnswerCount, string Sha256);

/// <summary>
/// Single place that finalises an exam session: locks it, fixes the submission receipt, grades the
/// answers (Module 11: MCQ / theory now, coding through the background grading queue) and runs the plagiarism
/// check, then tells the live monitor (SessionFinalized). Used by
/// student submit, teacher force-submit-all, the hub's per-student force-submit and the timeout
/// sweeper (SessionTimeoutService) so every path produces results and a receipt.
/// </summary>
public class SubmissionService
{
    // Absorbs network latency / clock drift for the student's last auto-save.
    public static readonly TimeSpan DeadlineGrace = TimeSpan.FromMinutes(2);

    private readonly AppDbContext _db;
    private readonly IHubContext<MonitoringHub> _hub;
    private readonly GradingService _grading;
    private readonly GradingQueue _gradingQueue;
    private readonly PlagiarismService _plagiarism;
    private readonly AuditService _audit;

    public SubmissionService(AppDbContext db, IHubContext<MonitoringHub> hub, GradingService grading, GradingQueue gradingQueue,
        PlagiarismService plagiarism, AuditService audit)
    {
        _db = db;
        _audit = audit;
        _hub = hub;
        _grading = grading;
        _gradingQueue = gradingQueue;
        _plagiarism = plagiarism;
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

        // MCQ and theory are graded here; coding answers get a Pending row and are queued (after the save, so the
        // worker never looks for a row that is not committed yet) — submit does not wait for any compiler.
        List<Guid> toGrade = session.Exam.AiEvaluationEnabled ? await _grading.StageAsync(answers) : [];

        // Practice exams: shared answers are expected, not misconduct
        if (!session.Exam.IsMock)
            await _plagiarism.DetectAsync(session, answers);

        // Module 12: the student's own submit is audited with its receipt (force-submit and time-out are audited by
        // the proctor action / the sweeper that finalise the session).
        if (finalStatus == SessionStatus.Submitted)
            _audit.Add(session.UserId, "SESSION_SUBMITTED", "ExamSession", session.SessionId.ToString(), new
            {
                examId = session.ExamId,
                answerCount = answers.Count,
                receiptSha256 = session.ReceiptSha256
            });

        await _db.SaveChangesAsync();
        foreach (var answerId in toGrade) _gradingQueue.Enqueue(answerId);

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
}
