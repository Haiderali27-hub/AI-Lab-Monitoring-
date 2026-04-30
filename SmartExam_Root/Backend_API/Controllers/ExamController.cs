using System.Security.Claims;
using Backend_API.Contracts;
using Backend_API.Data;
using Backend_API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

[ApiController]
[Route("api/exams")]
[Authorize]
public class ExamController(AppDbContext dbContext) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;

    [HttpGet("student/current")]
    [Authorize(Roles = nameof(SystemRole.Student))]
    public async Task<IActionResult> GetStudentCurrentExam(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var studentId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "User claim missing."));
        }

        var now = DateTime.UtcNow;

        var assignment = await _dbContext.ExamAssignments
            .Include(x => x.Exam)
            .Where(x => x.StudentUserId == studentId)
            .OrderBy(x => x.Exam.StartUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (assignment is null)
        {
            var unscheduled = new StudentExamStatusDto(
                null,
                "No Exam Assigned",
                ExamSessionStatus.NotStarted,
                null,
                null,
                0,
                false,
                "Student is not assigned to any exam.");

            return Ok(ApiResponse<StudentExamStatusDto>.Ok(unscheduled));
        }

        var existingSession = await _dbContext.ExamSessions
            .Where(x => x.ExamId == assignment.ExamId && x.StudentUserId == studentId)
            .OrderByDescending(x => x.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var remainingSeconds = Math.Max(0, (int)(assignment.Exam.EndUtc - now).TotalSeconds);
        var status = existingSession?.Status ?? ExamSessionStatus.NotStarted;
        var message = !assignment.IsEligible
            ? "Student is not eligible for this exam."
            : assignment.Exam.StartUtc > now
                ? "Exam has not started yet."
                : assignment.Exam.EndUtc < now
                    ? "Exam window has ended."
                    : "Student can proceed.";

        var response = new StudentExamStatusDto(
            assignment.ExamId,
            assignment.Exam.Name,
            status,
            assignment.Exam.StartUtc,
            assignment.Exam.EndUtc,
            remainingSeconds,
            assignment.IsEligible,
            message);

        return Ok(ApiResponse<StudentExamStatusDto>.Ok(response));
    }

    [HttpPost("student/start")]
    [Authorize(Roles = nameof(SystemRole.Student))]
    public async Task<IActionResult> StartStudentExam(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var studentId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "User claim missing."));
        }

        var now = DateTime.UtcNow;

        var assignment = await _dbContext.ExamAssignments
            .Include(x => x.Exam)
            .Where(x => x.StudentUserId == studentId && x.IsEligible)
            .OrderBy(x => x.Exam.StartUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (assignment is null)
        {
            return BadRequest(ApiResponse<object>.Fail("NOT_ELIGIBLE", "No eligible exam assignment found."));
        }

        if (assignment.Exam.StartUtc > now || assignment.Exam.EndUtc < now)
        {
            return BadRequest(ApiResponse<object>.Fail("OUTSIDE_EXAM_WINDOW", "Exam cannot be started outside its schedule."));
        }

        var existing = await _dbContext.ExamSessions
            .FirstOrDefaultAsync(
                x => x.ExamId == assignment.ExamId &&
                     x.StudentUserId == studentId &&
                     x.Status == ExamSessionStatus.InProgress,
                cancellationToken);

        if (existing is not null)
        {
            return Ok(ApiResponse<object>.Ok(new
            {
                existing.Id,
                existing.Status,
                existing.StartedAtUtc
            }, "Exam already in progress."));
        }

        var session = new ExamSession
        {
            ExamId = assignment.ExamId,
            StudentUserId = studentId,
            Status = ExamSessionStatus.InProgress,
            StartedAtUtc = now
        };

        _dbContext.ExamSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<object>.Ok(new
        {
            session.Id,
            session.Status,
            session.StartedAtUtc
        }, "Exam session started."));
    }

    [HttpGet("admin/live-roster")]
    [Authorize(Roles = $"{nameof(SystemRole.OrganizationAdmin)},{nameof(SystemRole.Teacher)}")]
    public async Task<IActionResult> GetLiveRoster(CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));
        }

        var students = await _dbContext.Users
            .Where(x => x.InstitutionId == institutionId && x.Role == SystemRole.Student)
            .Select(x => new { x.Id, x.Username })
            .ToListAsync(cancellationToken);

        var studentIds = students.Select(x => x.Id).ToList();
        var sessions = await _dbContext.ExamSessions
            .Include(x => x.Exam)
            .Where(x => studentIds.Contains(x.StudentUserId))
            .ToListAsync(cancellationToken);

        var heartbeatEvents = await _dbContext.MonitoringEvents
            .Where(x => studentIds.Contains(x.StudentUserId) && x.EventType == "Heartbeat")
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;

        var roster = students.Select(student =>
        {
            var latestSession = sessions
                .Where(x => x.StudentUserId == student.Id)
                .OrderByDescending(x => x.StartedAtUtc)
                .FirstOrDefault();

            var latestHeartbeat = heartbeatEvents
                .Where(x => x.StudentUserId == student.Id)
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefault();

            var remainingSeconds = latestSession?.Exam is not null
                ? Math.Max(0, (int)(latestSession.Exam.EndUtc - now).TotalSeconds)
                : 0;

            var isOnline = latestHeartbeat is not null && (now - latestHeartbeat.CreatedAtUtc).TotalSeconds <= 30;

            return new LiveRosterItemDto(
                student.Id,
                student.Username,
                latestSession?.Status ?? ExamSessionStatus.NotStarted,
                remainingSeconds,
                latestHeartbeat?.CreatedAtUtc,
                isOnline);
        }).ToList();

        return Ok(ApiResponse<IReadOnlyList<LiveRosterItemDto>>.Ok(roster));
    }

    private bool TryGetUserId(out Guid userId)
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }

    private bool TryGetInstitutionId(out Guid institutionId)
    {
        var claimValue = User.FindFirstValue("institutionId");
        return Guid.TryParse(claimValue, out institutionId);
    }
}