using Backend_API.Data;
using Backend_API.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

[ApiController]
[Route("api/exams")]
[Authorize]
public class ExamsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ExamsController(AppDbContext db) => _db = db;

    // GET /api/exams  — Teacher sees their exams; Student sees assigned exams
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        if (role == "Student")
        {
            var exams = await _db.ExamAssignments
                .Where(a => a.UserId == userId && a.IsEligible)
                .Include(a => a.Exam).ThenInclude(e => e.Section).ThenInclude(s => s.Course)
                .Select(a => new
                {
                    a.Exam.ExamId,
                    a.Exam.Title,
                    CourseName = a.Exam.Section.Course.Name,
                    a.Exam.StartTime,
                    a.Exam.DurationMinutes,
                    a.Exam.Status,
                    a.IsEligible,
                    WorkstationNumber = a.Workstation != null ? a.Workstation.MachineNumber : null
                })
                .ToListAsync();
            return Ok(exams);
        }

        // Teacher / Admin — see all exams
        var allExams = await _db.Exams
            .Include(e => e.Section).ThenInclude(s => s.Course)
            .Include(e => e.Questions)
            .Select(e => new
            {
                e.ExamId, e.Title, e.StartTime, e.DurationMinutes, e.Status,
                CourseName = e.Section.Course.Name,
                SectionName = e.Section.Name,
                QuestionCount = e.Questions.Count
            })
            .ToListAsync();
        return Ok(allExams);
    }

    // GET /api/exams/{id}  — Full exam detail including questions
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var exam = await _db.Exams
            .Include(e => e.Questions).ThenInclude(q => q.TestCases)
            .Include(e => e.Assignments).ThenInclude(a => a.Student)
            .Include(e => e.Section).ThenInclude(s => s.Course)
            .FirstOrDefaultAsync(e => e.ExamId == id);

        if (exam is null) return NotFound();

        // Students only see non-hidden test cases
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (role == "Student")
        {
            foreach (var q in exam.Questions)
                q.TestCases = q.TestCases.Where(tc => !tc.IsHidden).ToList();
        }

        return Ok(exam);
    }

    // POST /api/exams/{id}/start-session  — Student starts the exam
    [HttpPost("{id:guid}/start-session")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> StartSession(Guid id)
    {
        var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);

        var assignment = await _db.ExamAssignments
            .Include(a => a.Exam)
            .FirstOrDefaultAsync(a => a.ExamId == id && a.UserId == userId);

        if (assignment is null) return Forbid();
        if (!assignment.IsEligible)
            return BadRequest(new { message = "You are not eligible for this exam." });
        if (assignment.Exam.Status == ExamStatus.Ended)
            return BadRequest(new { message = "This exam has already ended." });

        // Check if session already exists
        var existing = await _db.ExamSessions
            .FirstOrDefaultAsync(s => s.ExamId == id && s.UserId == userId);
        if (existing is not null)
            return Ok(new { existing.SessionId, message = "Session already active." });

        // Activate exam if it is the first student starting
        if (assignment.Exam.Status == ExamStatus.Scheduled)
        {
            assignment.Exam.Status = ExamStatus.Active;
        }

        var session = new Backend_API.Models.ExamSession
        {
            ExamId = id,
            UserId = userId,
            Status = SessionStatus.InProgress
        };

        _db.ExamSessions.Add(session);
        await _db.SaveChangesAsync();

        return Ok(new { session.SessionId });
    }

    // POST /api/exams/sessions/{sessionId}/submit  — Student submits
    [HttpPost("sessions/{sessionId:guid}/submit")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Submit(Guid sessionId)
    {
        var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);

        var session = await _db.ExamSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.UserId == userId);

        if (session is null) return NotFound();
        if (session.Status != SessionStatus.InProgress)
            return BadRequest(new { message = "Session is not active." });

        session.Status = SessionStatus.Submitted;
        session.SubmittedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Exam submitted successfully.", submittedAt = session.SubmittedAt });
    }

    // POST /api/exams/sessions/{sessionId}/save-answer  — Auto-save
    [HttpPost("sessions/{sessionId:guid}/save-answer")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> SaveAnswer(Guid sessionId, [FromBody] SaveAnswerRequest req)
    {
        var session = await _db.ExamSessions.FindAsync(sessionId);
        if (session is null || session.Status != SessionStatus.InProgress)
            return BadRequest(new { message = "Invalid or inactive session." });

        var answer = await _db.Answers
            .FirstOrDefaultAsync(a => a.SessionId == sessionId && a.QuestionId == req.QuestionId);

        if (answer is null)
        {
            answer = new Backend_API.Models.Answer
            {
                SessionId = sessionId,
                QuestionId = req.QuestionId,
                AnswerText = req.AnswerText,
                LastSavedAt = DateTime.UtcNow
            };
            _db.Answers.Add(answer);
        }
        else
        {
            answer.AnswerText = req.AnswerText;
            answer.LastSavedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "Answer saved.", answer.LastSavedAt });
    }

    // POST /api/exams/sessions/{sessionId}/monitoring-event
    [HttpPost("sessions/{sessionId:guid}/monitoring-event")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> RecordMonitoringEvent(Guid sessionId, [FromBody] MonitoringEventRequest req)
    {
        var session = await _db.ExamSessions.FindAsync(sessionId);
        if (session is null) return NotFound();

        _db.MonitoringEvents.Add(new Backend_API.Models.MonitoringEvent
        {
            ExamSessionId = sessionId,
            EventType = req.EventType,
            Payload = req.Payload
        });

        await _db.SaveChangesAsync();
        return Ok(new { message = "Event recorded." });
    }
}

public record SaveAnswerRequest(Guid QuestionId, string AnswerText);
public record MonitoringEventRequest(Backend_API.Models.Enums.MonitoringEventType EventType, string Payload);
