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
            .ThenInclude(x => x.Lab)
            .Include(x => x.Exam)
            .ThenInclude(x => x.ProctorUser)
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
                "Student is not assigned to any exam.",
                null,
                null,
                null);

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
            message,
            assignment.Exam.Instructions,
            assignment.Exam.Lab?.Name,
            assignment.Exam.ProctorUser?.Username);

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

    [HttpGet]
    [Authorize(Roles = "OrganizationAdmin,Teacher")]
    public async Task<IActionResult> GetExams(CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));
        }

        var now = DateTime.UtcNow;

        var exams = await _dbContext.Exams
            .Include(x => x.Lab)
            .Include(x => x.ProctorUser)
            .Include(x => x.Assignments)
            .Where(x => x.InstitutionId == institutionId)
            .OrderByDescending(x => x.StartUtc)
            .ToListAsync(cancellationToken);

        var response = exams.Select(exam =>
        {
            var candidateCount = exam.Assignments.Count;
            var eligibleCount = exam.Assignments.Count(x => x.IsEligible);
            var ineligibleCount = candidateCount - eligibleCount;

            return new ExamSummaryDto(
                exam.Id,
                exam.Name,
                exam.StartUtc,
                exam.EndUtc,
                ResolveExamStatus(exam, now),
                exam.IsActive,
                exam.Lab?.Name,
                exam.LabId,
                exam.ProctorUser?.Username,
                exam.ProctorUserId,
                candidateCount,
                eligibleCount,
                ineligibleCount,
                exam.Instructions);
        }).ToList();

        return Ok(ApiResponse<IReadOnlyList<ExamSummaryDto>>.Ok(response));
    }

    [HttpGet("candidates")]
    [Authorize(Roles = "OrganizationAdmin,Teacher")]
    public async Task<IActionResult> GetCandidates(CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));
        }

        var candidates = await _dbContext.Users
            .Where(x => x.InstitutionId == institutionId && x.Role == SystemRole.Student)
            .Select(x => new ExamCandidateDto(
                x.Id,
                x.Username,
                x.Email,
                x.DeviceBindings.Any(),
                x.IsActive))
            .OrderBy(x => x.Username)
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<ExamCandidateDto>>.Ok(candidates));
    }

    [HttpGet("proctors")]
    [Authorize(Roles = "OrganizationAdmin,Teacher")]
    public async Task<IActionResult> GetProctors(CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));
        }

        var proctors = await _dbContext.Users
            .Where(x => x.InstitutionId == institutionId && x.Role == SystemRole.Teacher)
            .Select(x => new ProctorDto(x.Id, x.Username, x.Email))
            .OrderBy(x => x.Username)
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<ProctorDto>>.Ok(proctors));
    }

    [HttpPost]
    [Authorize(Roles = "OrganizationAdmin,Teacher")]
    public async Task<IActionResult> CreateExam(
        [FromBody] CreateExamRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));
        }

        if (request.EndUtc <= request.StartUtc)
        {
            return BadRequest(ApiResponse<object>.Fail("INVALID_TIME", "Exam end time must be after start time."));
        }

        if (request.LabId is not null)
        {
            var labExists = await _dbContext.Labs
                .AnyAsync(x => x.Id == request.LabId && x.InstitutionId == institutionId, cancellationToken);
            if (!labExists)
            {
                return BadRequest(ApiResponse<object>.Fail("LAB_NOT_FOUND", "Selected lab was not found."));
            }
        }

        if (request.ProctorUserId is not null)
        {
            var proctorExists = await _dbContext.Users
                .AnyAsync(x => x.Id == request.ProctorUserId && x.InstitutionId == institutionId && x.Role == SystemRole.Teacher, cancellationToken);
            if (!proctorExists)
            {
                return BadRequest(ApiResponse<object>.Fail("PROCTOR_NOT_FOUND", "Selected proctor was not found."));
            }
        }

        var exam = new Exam
        {
            InstitutionId = institutionId,
            Name = request.Name.Trim(),
            StartUtc = request.StartUtc,
            EndUtc = request.EndUtc,
            LabId = request.LabId,
            ProctorUserId = request.ProctorUserId,
            Instructions = string.IsNullOrWhiteSpace(request.Instructions) ? null : request.Instructions.Trim(),
            IsActive = true
        };

        _dbContext.Exams.Add(exam);

        var incomingAssignments = request.Assignments ?? Array.Empty<ExamAssignmentRequest>();
        var assignmentIds = new HashSet<Guid>();
        var requestedIds = incomingAssignments
            .Select(x => x.StudentId)
            .Distinct()
            .ToList();

        var validStudentIds = await _dbContext.Users
            .Where(x => x.InstitutionId == institutionId && x.Role == SystemRole.Student && requestedIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var assignment in incomingAssignments)
        {
            if (!validStudentIds.Contains(assignment.StudentId) || assignmentIds.Contains(assignment.StudentId))
            {
                continue;
            }

            assignmentIds.Add(assignment.StudentId);
            _dbContext.ExamAssignments.Add(new ExamAssignment
            {
                Exam = exam,
                StudentUserId = assignment.StudentId,
                IsEligible = assignment.IsEligible
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetExams), ApiResponse<object>.Ok(new { exam.Id }));
    }

    [HttpPut("{examId:guid}")]
    [Authorize(Roles = "OrganizationAdmin,Teacher")]
    public async Task<IActionResult> UpdateExam(
        Guid examId,
        [FromBody] UpdateExamRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));
        }

        var exam = await _dbContext.Exams
            .FirstOrDefaultAsync(x => x.Id == examId && x.InstitutionId == institutionId, cancellationToken);

        if (exam is null)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Exam not found."));
        }

        if (request.EndUtc <= request.StartUtc)
        {
            return BadRequest(ApiResponse<object>.Fail("INVALID_TIME", "Exam end time must be after start time."));
        }

        if (request.LabId is not null)
        {
            var labExists = await _dbContext.Labs
                .AnyAsync(x => x.Id == request.LabId && x.InstitutionId == institutionId, cancellationToken);
            if (!labExists)
            {
                return BadRequest(ApiResponse<object>.Fail("LAB_NOT_FOUND", "Selected lab was not found."));
            }
        }

        if (request.ProctorUserId is not null)
        {
            var proctorExists = await _dbContext.Users
                .AnyAsync(x => x.Id == request.ProctorUserId && x.InstitutionId == institutionId && x.Role == SystemRole.Teacher, cancellationToken);
            if (!proctorExists)
            {
                return BadRequest(ApiResponse<object>.Fail("PROCTOR_NOT_FOUND", "Selected proctor was not found."));
            }
        }

        exam.Name = request.Name.Trim();
        exam.StartUtc = request.StartUtc;
        exam.EndUtc = request.EndUtc;
        exam.LabId = request.LabId;
        exam.ProctorUserId = request.ProctorUserId;
        exam.Instructions = string.IsNullOrWhiteSpace(request.Instructions) ? null : request.Instructions.Trim();
        exam.IsActive = request.IsActive;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<object>.Ok(new { exam.Id }));
    }

    [HttpPut("{examId:guid}/assignments")]
    [Authorize(Roles = "OrganizationAdmin,Teacher")]
    public async Task<IActionResult> UpdateAssignments(
        Guid examId,
        [FromBody] UpdateExamAssignmentsRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));
        }

        var examExists = await _dbContext.Exams
            .AnyAsync(x => x.Id == examId && x.InstitutionId == institutionId, cancellationToken);

        if (!examExists)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Exam not found."));
        }

        var incomingAssignments = request.Assignments ?? Array.Empty<ExamAssignmentRequest>();
        var requestedIds = incomingAssignments
            .Select(x => x.StudentId)
            .Distinct()
            .ToList();

        var validStudentIds = await _dbContext.Users
            .Where(x => x.InstitutionId == institutionId && x.Role == SystemRole.Student && requestedIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var existingAssignments = await _dbContext.ExamAssignments
            .Where(x => x.ExamId == examId)
            .ToListAsync(cancellationToken);

        _dbContext.ExamAssignments.RemoveRange(existingAssignments);

        var assigned = new HashSet<Guid>();
        foreach (var assignment in incomingAssignments)
        {
            if (!validStudentIds.Contains(assignment.StudentId) || assigned.Contains(assignment.StudentId))
            {
                continue;
            }

            assigned.Add(assignment.StudentId);
            _dbContext.ExamAssignments.Add(new ExamAssignment
            {
                ExamId = examId,
                StudentUserId = assignment.StudentId,
                IsEligible = assignment.IsEligible
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<object>.Ok(new { examId }));
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
}