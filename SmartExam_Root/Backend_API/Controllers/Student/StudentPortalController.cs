using Backend_API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Backend_API.Controllers.Student;

[ApiController]
[Route("api/student")]
[Authorize(Roles = "Student")]
public class StudentPortalController : ControllerBase
{
    private readonly AppDbContext _db;
    public StudentPortalController(AppDbContext db) => _db = db;

    // Helper — gets the logged-in student's ID from JWT
    private Guid Me => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    // GET /api/student/dashboard
    // Returns summary stats + recent exams for the student's personal dashboard
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        var studentId = Me;

        var sessions = await _db.ExamSessions
            .Where(s => s.UserId == studentId && s.Status != Models.Enums.SessionStatus.InProgress)
            .Include(s => s.Exam).ThenInclude(e => e.Section).ThenInclude(sec => sec.Course)
            .Include(s => s.Exam).ThenInclude(e => e.Questions)
            .Include(s => s.Answers).ThenInclude(a => a.AiGradingResult)
            .Include(s => s.Answers).ThenInclude(a => a.TeacherGradeOverride)
            .Include(s => s.MonitoringEvents)
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync();

        var examResults = sessions.Select(s =>
        {
            int total = s.Exam.Questions.Sum(q => q.Marks);
            double earned = s.Answers.Sum(a =>
                a.TeacherGradeOverride?.FinalMarks ?? a.AiGradingResult?.SuggestedMarks ?? 0);
            double pct = total > 0 ? Math.Round((earned / total) * 100, 1) : 0;
            int violations = s.MonitoringEvents.Count(e => e.EventType == Models.Enums.MonitoringEventType.Violation);

            return new
            {
                s.SessionId,
                s.Exam.ExamId,
                s.Exam.Title,
                CourseName = s.Exam.Section.Course.Name,
                s.StartedAt,
                s.SubmittedAt,
                s.Status,
                TotalMarks = total,
                EarnedMarks = Math.Round(earned, 1),
                ScorePercent = pct,
                ViolationCount = violations,
                Passed = pct >= 50
            };
        }).ToList();

        // Performance trend — score percent per exam over time
        var trend = examResults
            .OrderBy(r => r.StartedAt)
            .Select(r => new { r.Title, r.ScorePercent, r.StartedAt })
            .ToList();

        return Ok(new
        {
            TotalExamsTaken = examResults.Count,
            AverageScore = examResults.Any() ? Math.Round(examResults.Average(r => r.ScorePercent), 1) : 0,
            ExamsPassed = examResults.Count(r => r.Passed),
            TotalViolations = examResults.Sum(r => r.ViolationCount),
            RecentExams = examResults.Take(5),
            PerformanceTrend = trend
        });
    }

    // GET /api/student/exams
    // All exams the student has taken or is assigned to
    [HttpGet("exams")]
    public async Task<IActionResult> GetMyExams()
    {
        var studentId = Me;

        var assignments = await _db.ExamAssignments
            .Where(a => a.UserId == studentId)
            .Include(a => a.Exam).ThenInclude(e => e.Section).ThenInclude(s => s.Course)
            .Include(a => a.Exam).ThenInclude(e => e.Questions)
            .Select(a => new
            {
                a.Exam.ExamId,
                a.Exam.Title,
                CourseName = a.Exam.Section.Course.Name,
                SectionName = a.Exam.Section.Name,
                a.Exam.StartTime,
                a.Exam.DurationMinutes,
                a.Exam.Status,
                a.IsEligible,
                QuestionCount = a.Exam.Questions.Count,
                TotalMarks = a.Exam.Questions.Sum(q => q.Marks)
            })
            .OrderByDescending(a => a.StartTime)
            .ToListAsync();

        return Ok(assignments);
    }

    // GET /api/student/exams/{examId}/result
    // Full result for one exam — answers, AI feedback, marks
    [HttpGet("exams/{examId:guid}/result")]
    public async Task<IActionResult> GetMyExamResult(Guid examId)
    {
        var studentId = Me;

        var session = await _db.ExamSessions
            .Where(s => s.ExamId == examId && s.UserId == studentId)
            .Include(s => s.Exam).ThenInclude(e => e.Questions)
            .Include(s => s.Answers).ThenInclude(a => a.Question)
            .Include(s => s.Answers).ThenInclude(a => a.AiGradingResult)
            .Include(s => s.Answers).ThenInclude(a => a.TeacherGradeOverride)
            .FirstOrDefaultAsync();

        if (session is null)
            return NotFound(new { message = "No result found for this exam." });

        // Students can only see results after exam ends
        if (session.Exam.Status != Models.Enums.ExamStatus.Ended)
            return BadRequest(new { message = "Results are not available until the exam ends." });

        var answers = session.Answers.Select(a =>
        {
            double marks = a.TeacherGradeOverride?.FinalMarks ?? a.AiGradingResult?.SuggestedMarks ?? 0;
            return new
            {
                a.AnswerId,
                QuestionText = a.Question.BodyText,
                QuestionType = a.Question.Type.ToString(),
                TotalMarks = a.Question.Marks,
                a.AnswerText,
                EarnedMarks = marks,
                AiFeedback = a.AiGradingResult == null ? null : new
                {
                    a.AiGradingResult.SuggestedMarks,
                    a.AiGradingResult.Justification,
                    a.AiGradingResult.Confidence
                },
                TeacherOverridden = a.TeacherGradeOverride != null
            };
        }).ToList();

        int totalMarks = session.Exam.Questions.Sum(q => q.Marks);
        double totalEarned = answers.Sum(a => a.EarnedMarks);

        return Ok(new
        {
            session.SessionId,
            session.Exam.Title,
            session.StartedAt,
            session.SubmittedAt,
            session.Status,
            TotalMarks = totalMarks,
            EarnedMarks = Math.Round(totalEarned, 1),
            ScorePercent = totalMarks > 0 ? Math.Round((totalEarned / totalMarks) * 100, 1) : 0,
            Passed = totalMarks > 0 && (totalEarned / totalMarks) >= 0.5,
            Answers = answers
        });
    }

    // GET /api/student/violations
    // Student's own violation history across all exams
    [HttpGet("violations")]
    public async Task<IActionResult> GetMyViolations()
    {
        var studentId = Me;

        var violations = await _db.MonitoringEvents
            .Where(e => e.ExamSession.UserId == studentId
                     && e.EventType == Models.Enums.MonitoringEventType.Violation)
            .Include(e => e.ExamSession).ThenInclude(s => s.Exam)
            .OrderByDescending(e => e.RecordedAt)
            .Select(e => new
            {
                e.EventId,
                ExamTitle = e.ExamSession.Exam.Title,
                e.RecordedAt,
                e.Payload
            })
            .ToListAsync();

        return Ok(new
        {
            TotalViolations = violations.Count,
            Violations = violations
        });
    }

    // GET /api/student/performance-trend
    // Score percent for each completed exam, ordered by date — used for line chart
    [HttpGet("performance-trend")]
    public async Task<IActionResult> GetPerformanceTrend()
    {
        var studentId = Me;

        var sessions = await _db.ExamSessions
            .Where(s => s.UserId == studentId && s.Status != Models.Enums.SessionStatus.InProgress)
            .Include(s => s.Exam).ThenInclude(e => e.Questions)
            .Include(s => s.Answers).ThenInclude(a => a.AiGradingResult)
            .Include(s => s.Answers).ThenInclude(a => a.TeacherGradeOverride)
            .OrderBy(s => s.StartedAt)
            .ToListAsync();

        var trend = sessions.Select(s =>
        {
            int total = s.Exam.Questions.Sum(q => q.Marks);
            double earned = s.Answers.Sum(a =>
                a.TeacherGradeOverride?.FinalMarks ?? a.AiGradingResult?.SuggestedMarks ?? 0);
            return new
            {
                Title = s.Exam.Title,
                StartedAt = s.StartedAt,
                Date = s.StartedAt.ToString("MMM dd"),
                ScorePercent = total > 0 ? Math.Round((earned / total) * 100, 1) : 0
            };
        });

        return Ok(trend);
    }

    // GET /api/student/notifications
    // Student's own notifications (exam reminders, grade releases etc.)
    [HttpGet("notifications")]
    public async Task<IActionResult> GetMyNotifications()
    {
        var studentId = Me;

        var notifications = await _db.Notifications
            .Where(n => n.RecipientId == studentId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new
            {
                n.NotificationId,
                n.Title,
                n.Body,
                n.Type,
                n.IsRead,
                n.CreatedAt,
                n.RelatedEntityId
            })
            .ToListAsync();

        return Ok(new
        {
            UnreadCount = notifications.Count(n => !n.IsRead),
            Notifications = notifications
        });
    }

    // PATCH /api/student/notifications/{id}/read
    // Mark a notification as read
    [HttpPatch("notifications/{notificationId:guid}/read")]
    public async Task<IActionResult> MarkNotificationRead(Guid notificationId)
    {
        var notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.NotificationId == notificationId && n.RecipientId == Me);

        if (notification is null) return NotFound();

        notification.IsRead = true;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Marked as read." });
    }
}
