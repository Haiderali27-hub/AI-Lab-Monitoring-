using Backend_API.Data;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Backend_API.Services;
using Backend_API.Services.Grading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

/// <summary>
/// Module 11 — grading actions beyond the per-answer override: the class-wide plagiarism matrix (heat map), accepting
/// every AI suggestion at once, and re-running the grader on one answer. All of them are the exam owner's (section
/// teacher) or an admin's: proctors supervise but never see or change grades. Bulk accept and regrade are audited.
/// </summary>
[ApiController]
[Route("api")]
[Authorize(Roles = "Teacher,Admin,SuperAdmin")]
public class GradingController(
    AppDbContext db,
    ExamAccessService access,
    AuditService audit,
    PlagiarismService plagiarism,
    GradingService grading,
    GradingQueue queue) : ControllerBase
{
    private Guid CurrentUserId => ExamAccessService.UserIdOf(User)!.Value;

    private static readonly SessionStatus[] Finalised = [SessionStatus.Submitted, SessionStatus.ForceSubmitted, SessionStatus.TimedOut];

    // GET /api/exams/{examId}/plagiarism/matrix?questionId=  → { students, questions, matrix }
    [HttpGet("exams/{examId:guid}/plagiarism/matrix")]
    public async Task<IActionResult> GetMatrix(Guid examId, [FromQuery] Guid? questionId)
    {
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.ExamId == examId);
        if (exam is null) return NotFound(new { message = "Exam not found." });
        if (await access.GetAsync(User, examId) < ExamAccess.Owner) return Forbidden();
        if (exam.IsMock)
            return BadRequest(new { code = "MOCK_EXAM", message = "Practice exams are not checked for plagiarism." });
        if (questionId is { } qid && !await db.Questions.AnyAsync(q => q.QuestionId == qid && q.ExamId == examId && q.Type != QuestionType.Mcq))
            return BadRequest(new { code = "INVALID_QUESTION", message = "Choose a coding or theory question of this exam." });

        var matrix = await plagiarism.BuildMatrixAsync(examId, questionId);
        return Ok(new
        {
            examId,
            questionId,
            students = matrix.Students.Select(s => new { s.UserId, s.Name, s.SubmittedAt }),
            questions = matrix.Questions.Select(q => new { q.QuestionId, q.Type, q.OrderIndex, q.BodyText }),
            matrix = matrix.Matrix,
            threshold = exam.PlagiarismThreshold
        });
    }

    // POST /api/exams/{examId}/grading/accept-ai  → overrides equal to the suggestion for every graded, un-overridden answer.
    // Answers the grader could not mark (not compiled, no model answer, compile check only) are left for a manual mark:
    // their "0, teacher review required" is not a suggestion, and accepting it in bulk would silently fail the student.
    [HttpPost("exams/{examId:guid}/grading/accept-ai")]
    public async Task<IActionResult> AcceptAiSuggestions(Guid examId)
    {
        if (!await db.Exams.AnyAsync(e => e.ExamId == examId)) return NotFound(new { message = "Exam not found." });
        if (await access.GetAsync(User, examId) < ExamAccess.Owner) return Forbidden();

        var candidates = await db.Answers
            .Include(a => a.AiGradingResult).ThenInclude(g => g!.TestCaseResults)
            .Where(a => a.ExamSession.ExamId == examId &&
                        Finalised.Contains(a.ExamSession.Status) &&
                        a.TeacherGradeOverride == null &&
                        a.AiGradingResult != null)
            .ToListAsync();

        var graded = candidates.Where(a => a.AiGradingResult!.Status == AiGradingResult.StatusGraded).ToList();
        var accepted = graded.Where(a => !GradingService.NeedsManualMark(a.AiGradingResult!)).ToList();
        var needsManualMark = graded.Count - accepted.Count;
        var now = DateTime.UtcNow;
        foreach (var answer in accepted)
        {
            db.TeacherGradeOverrides.Add(new TeacherGradeOverride
            {
                AnswerId = answer.AnswerId,
                TeacherId = CurrentUserId,
                FinalMarks = answer.AiGradingResult!.SuggestedMarks,
                Note = "Accepted AI suggestion",
                OverriddenAt = now
            });
        }
        var notReady = candidates.Count - graded.Count;
        audit.Add(CurrentUserId, "AI_SUGGESTIONS_ACCEPTED", "Exam", examId.ToString(), new { accepted = accepted.Count, notReady, needsManualMark });
        await db.SaveChangesAsync();

        var message = $"Accepted {accepted.Count} suggestion(s).";
        if (notReady > 0) message += $" {notReady} answer(s) are still being graded or failed and need a manual mark.";
        if (needsManualMark > 0) message += $" {needsManualMark} answer(s) could not be graded automatically and need a manual mark.";
        return Ok(new
        {
            accepted = accepted.Count,
            notReady,
            needsManualMark,
            message
        });
    }

    // POST /api/answers/{answerId}/regrade  → re-queues one coding / theory answer
    [HttpPost("answers/{answerId:guid}/regrade")]
    public async Task<IActionResult> Regrade(Guid answerId)
    {
        var answer = await db.Answers
            .Include(a => a.Question)
            .Include(a => a.ExamSession)
            .FirstOrDefaultAsync(a => a.AnswerId == answerId);
        if (answer is null) return NotFound(new { message = "Answer not found." });
        if (await access.GetAsync(User, answer.ExamSession.ExamId) < ExamAccess.Owner) return Forbidden();
        if (!Finalised.Contains(answer.ExamSession.Status))
            return Conflict(new { code = "SESSION_NOT_FINAL", message = "The answer can be regraded once the session is submitted." });

        if (!await grading.RequeueAsync(answer))
            return BadRequest(new { code = "NOT_REGRADABLE", message = "Multiple-choice answers are graded exactly and cannot be regraded." });

        audit.Add(CurrentUserId, "AI_REGRADE_REQUESTED", "Answer", answerId.ToString(), new
        {
            examId = answer.ExamSession.ExamId,
            sessionId = answer.SessionId,
            questionId = answer.QuestionId
        });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // The worker finished this answer in the same instant: the click raced it, nothing was changed.
            return Conflict(new { code = "GRADING_CHANGED", message = "The grade was just updated. Reload and try again." });
        }
        queue.Enqueue(answerId);
        return Ok(new { answerId, status = AiGradingResult.StatusPending, message = "Answer queued for grading." });
    }

    private ObjectResult Forbidden() =>
        StatusCode(StatusCodes.Status403Forbidden, new { message = "Only the exam's teacher or an admin can see or change its grades." });
}
