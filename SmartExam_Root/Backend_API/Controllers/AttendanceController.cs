using System.Security.Claims;
using System.Text;
using Backend_API.Data;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Backend_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

/// <summary>
/// Module 9 — class attendance per section, imported from the teacher's CSV. Eligibility is then
/// recalculated per exam (POST /api/exams/{id}/eligibility/recalculate). Teachers may only touch
/// sections they teach; admins any section.
/// </summary>
[ApiController]
[Route("api/sections/{sectionId:guid}/attendance")]
[Authorize(Roles = "Teacher,Admin,SuperAdmin")]
public class AttendanceController : ControllerBase
{
    public const long MaxUploadBytes = 1024 * 1024;

    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public AttendanceController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

    // 404 when the section doesn't exist, 403 when a teacher doesn't teach it, else null.
    private async Task<IActionResult?> AuthorizeSectionAsync(Guid sectionId)
    {
        var section = await _db.Sections.AsNoTracking().FirstOrDefaultAsync(s => s.SectionId == sectionId);
        if (section is null) return NotFound(new { message = "Section not found." });
        if (User.FindFirst(ClaimTypes.Role)?.Value == "Teacher" && section.TeacherId != CurrentUserId)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only manage attendance for your own sections." });
        return null;
    }

    // POST /api/sections/{sectionId}/attendance/import — multipart field "file" (CSV: email,total,attended)
    [HttpPost("import")]
    [RequestSizeLimit(MaxUploadBytes + 64 * 1024)]
    public async Task<IActionResult> Import(Guid sectionId, IFormFile? file)
    {
        var denied = await AuthorizeSectionAsync(sectionId);
        if (denied is not null) return denied;

        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Attach the attendance CSV as the 'file' field." });
        if (file.Length > MaxUploadBytes)
            return BadRequest(new { message = "The attendance file must be 1 MB or smaller." });

        string text;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            text = await reader.ReadToEndAsync();

        var parsed = AttendanceCsvParser.Parse(text);
        if (parsed.HeaderError is not null) return BadRequest(new { message = parsed.HeaderError });

        var emails = parsed.Rows.Where(r => r.Error is null).Select(r => r.Email.ToLowerInvariant()).Distinct().ToList();
        var users = await _db.Users
            .Where(u => emails.Contains(u.Email.ToLower()))
            .Select(u => new { u.UserId, u.Email, u.Role, u.IsActive })
            .ToListAsync();
        var userByEmail = users.ToDictionary(u => u.Email.ToLowerInvariant());

        var enrolled = (await _db.SectionEnrollments
            .Where(e => e.SectionId == sectionId)
            .Select(e => e.UserId)
            .ToListAsync()).ToHashSet();
        var existing = await _db.AttendanceRecords.Where(r => r.SectionId == sectionId).ToListAsync();
        var existingByUser = existing.ToDictionary(r => r.UserId);

        var errors = new List<AttendanceRowIssue>();
        var warnings = new List<AttendanceRowIssue>();
        int imported = 0, updated = 0;
        var now = DateTime.UtcNow;

        foreach (var row in parsed.Rows)
        {
            if (row.Error is not null)
            {
                errors.Add(new AttendanceRowIssue(row.Line, row.Email, row.Error));
                continue;
            }
            if (!userByEmail.TryGetValue(row.Email.ToLowerInvariant(), out var user) || user.Role != UserRole.Student)
            {
                errors.Add(new AttendanceRowIssue(row.Line, row.Email, "unknown student"));
                continue;
            }
            if (!user.IsActive)
            {
                errors.Add(new AttendanceRowIssue(row.Line, row.Email, "student account is deactivated"));
                continue;
            }
            // Imported anyway: students can be assigned to an exam without a formal section enrollment.
            if (!enrolled.Contains(user.UserId))
                warnings.Add(new AttendanceRowIssue(row.Line, row.Email, "not enrolled in this section"));

            if (existingByUser.TryGetValue(user.UserId, out var record))
            {
                record.TotalClasses = row.Total;
                record.AttendedClasses = row.Attended;
                record.UpdatedAt = now;
                record.ImportedBy = CurrentUserId;
                updated++;
            }
            else
            {
                record = new AttendanceRecord
                {
                    SectionId = sectionId,
                    UserId = user.UserId,
                    TotalClasses = row.Total,
                    AttendedClasses = row.Attended,
                    UpdatedAt = now,
                    ImportedBy = CurrentUserId
                };
                _db.AttendanceRecords.Add(record);
                existingByUser[user.UserId] = record;
                imported++;
            }
        }

        _audit.Add(CurrentUserId, "ATTENDANCE_IMPORTED", "Section", sectionId.ToString(), new
        {
            sectionId, fileName = file.FileName, imported, updated,
            rejected = errors.Count, warnings = warnings.Count
        });
        await _db.SaveChangesAsync();

        return Ok(new AttendanceImportResult(imported, updated, errors, warnings));
    }

    // GET /api/sections/{sectionId}/attendance — the imported rows, for review in the web panel
    [HttpGet]
    public async Task<IActionResult> GetAll(Guid sectionId)
    {
        var denied = await AuthorizeSectionAsync(sectionId);
        if (denied is not null) return denied;

        var rows = await _db.AttendanceRecords
            .Where(r => r.SectionId == sectionId)
            .Include(r => r.Student)
            .OrderBy(r => r.Student.Name)
            .Select(r => new
            {
                r.UserId,
                StudentName = r.Student.Name,
                StudentEmail = r.Student.Email,
                r.TotalClasses,
                r.AttendedClasses,
                r.UpdatedAt
            })
            .ToListAsync();

        return Ok(rows.Select(r => new
        {
            r.UserId, r.StudentName, r.StudentEmail, r.TotalClasses, r.AttendedClasses, r.UpdatedAt,
            AttendancePercent = EligibilityCalculator.Percent(r.AttendedClasses, r.TotalClasses)
        }));
    }
}

public record AttendanceRowIssue(int Line, string Email, string Reason);
public record AttendanceImportResult(int Imported, int Updated, List<AttendanceRowIssue> Errors, List<AttendanceRowIssue> Warnings);
