using System.Security.Claims;
using System.Text.Json;
using Backend_API.Contracts;
using Backend_API.Data;
using Backend_API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = $"{nameof(SystemRole.OrganizationAdmin)},{nameof(SystemRole.Teacher)}")]
public class ReportsController(AppDbContext dbContext) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;

    [HttpGet("exams")]
    public async Task<IActionResult> GetExamReports(CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));
        }

        var exams = await _dbContext.Exams
            .Include(x => x.Assignments)
            .Where(x => x.InstitutionId == institutionId)
            .OrderByDescending(x => x.StartUtc)
            .ToListAsync(cancellationToken);

        var studentIds = await _dbContext.Users
            .Where(x => x.InstitutionId == institutionId && x.Role == SystemRole.Student)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var events = await _dbContext.MonitoringEvents
            .Where(x => studentIds.Contains(x.StudentUserId))
            .ToListAsync(cancellationToken);

        var sessionMap = await _dbContext.ExamSessions
            .Where(x => x.Exam.InstitutionId == institutionId)
            .Select(x => new { x.Id, x.ExamId })
            .ToDictionaryAsync(x => x.Id, x => x.ExamId, cancellationToken);

        var summaries = exams.Select(exam => BuildSummary(exam, events, sessionMap)).ToList();

        return Ok(ApiResponse<IReadOnlyList<ExamReportSummaryDto>>.Ok(summaries));
    }

    [HttpGet("exams/{examId:guid}")]
    public async Task<IActionResult> GetExamReport(Guid examId, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));
        }

        var exam = await _dbContext.Exams
            .Include(x => x.Assignments)
            .FirstOrDefaultAsync(x => x.Id == examId && x.InstitutionId == institutionId, cancellationToken);

        if (exam is null)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Exam not found."));
        }

        var studentIds = exam.Assignments.Select(x => x.StudentUserId).ToList();

        var students = await _dbContext.Users
            .Where(x => studentIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Username, x.Email, HasBinding = x.DeviceBindings.Any() })
            .ToListAsync(cancellationToken);

        var events = await _dbContext.MonitoringEvents
            .Where(x => studentIds.Contains(x.StudentUserId))
            .ToListAsync(cancellationToken);

        var attendance = GetEventIndex(events, "Attendance");
        var identity = GetEventIndex(events, "IdentityVerified");
        var lastHeartbeat = events
            .Where(x => x.EventType == "Heartbeat")
            .GroupBy(x => x.StudentUserId)
            .ToDictionary(x => x.Key, x => x.Max(e => e.CreatedAtUtc));

        var studentsDto = students.Select(student => new ExamReportStudentDto(
            student.Id,
            student.Username,
            student.Email,
            student.HasBinding,
            attendance.TryGetValue(student.Id, out var att) ? att : null,
            identity.TryGetValue(student.Id, out var id) ? id : null,
            lastHeartbeat.TryGetValue(student.Id, out var hb) ? hb : null
        )).ToList();

        var sessionMap = await _dbContext.ExamSessions
            .Where(x => x.ExamId == exam.Id)
            .Select(x => new { x.Id, x.ExamId })
            .ToDictionaryAsync(x => x.Id, x => x.ExamId, cancellationToken);

        var summary = BuildSummary(exam, events, sessionMap);

        var detail = new ExamReportDetailDto(
            summary.ExamId,
            summary.ExamName,
            summary.StartUtc,
            summary.EndUtc,
            summary.Status,
            summary.CandidateCount,
            summary.AttendanceCount,
            summary.IdentityVerifiedCount,
            summary.IncidentCount,
            studentsDto);

        return Ok(ApiResponse<ExamReportDetailDto>.Ok(detail));
    }

    private static ExamReportSummaryDto BuildSummary(
        Exam exam,
        IReadOnlyList<MonitoringEvent> events,
        IReadOnlyDictionary<Guid, Guid> sessionMap)
    {
        var attendance = events
            .Where(x => x.EventType == "Attendance" && MatchesExam(x, exam.Id, sessionMap))
            .Select(x => x.StudentUserId)
            .Distinct()
            .Count();

        var identity = events
            .Where(x => x.EventType == "IdentityVerified" && MatchesExam(x, exam.Id, sessionMap))
            .Select(x => x.StudentUserId)
            .Distinct()
            .Count();

        var incidents = events
            .Where(x => x.EventType == "Alert" && MatchesExam(x, exam.Id, sessionMap))
            .Count();

        return new ExamReportSummaryDto(
            exam.Id,
            exam.Name,
            exam.StartUtc,
            exam.EndUtc,
            ResolveExamStatus(exam, DateTime.UtcNow),
            exam.Assignments.Count,
            attendance,
            identity,
            incidents);
    }

    private static Dictionary<Guid, DateTime> GetEventIndex(IEnumerable<MonitoringEvent> events, string eventType)
    {
        return events
            .Where(x => x.EventType == eventType)
            .GroupBy(x => x.StudentUserId)
            .ToDictionary(x => x.Key, x => x.Max(e => e.CreatedAtUtc));
    }

    private static bool MatchesExam(
        MonitoringEvent monitoringEvent,
        Guid examId,
        IReadOnlyDictionary<Guid, Guid> sessionMap)
    {
        if (monitoringEvent.ExamSessionId is not null)
        {
            return sessionMap.TryGetValue(monitoringEvent.ExamSessionId.Value, out var mappedExamId) && mappedExamId == examId;
        }

        if (string.IsNullOrWhiteSpace(monitoringEvent.PayloadJson))
        {
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<EventExamPayload>(monitoringEvent.PayloadJson);
            return payload?.ExamId == examId;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveExamStatus(Exam exam, DateTime now)
    {
        if (!exam.IsActive)
        {
            return "Draft";
        }

        if (exam.StartUtc > now)
        {
            return "Scheduled";
        }

        if (exam.EndUtc < now)
        {
            return "Completed";
        }

        return "Live";
    }

    private bool TryGetInstitutionId(out Guid institutionId)
    {
        var claimValue = User.FindFirstValue("institutionId");
        return Guid.TryParse(claimValue, out institutionId);
    }

    private sealed record EventExamPayload(Guid ExamId);
}
