using Backend_API.Data;
using Backend_API.Models;
using Backend_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

/// <summary>
/// Module 7 — courses of a department. A course code (stored trimmed and upper-case, e.g. "CS301")
/// is unique within its department. Staff read; only Admin / SuperAdmin change. A course that still
/// has sections cannot be deleted (sections own exams, enrolments and attendance).
/// </summary>
[ApiController]
[Route("api/courses")]
[Authorize]
public class CoursesController : ControllerBase
{
    public const int MaxNameLength = 128;
    public const int MaxCodeLength = 32;

    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public CoursesController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);

    // GET /api/courses?deptId= — all courses, or one department's
    [HttpGet]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin,Invigilator")]
    public async Task<IActionResult> GetAll([FromQuery] Guid? deptId)
    {
        var query = _db.Courses.AsQueryable();
        if (deptId.HasValue) query = query.Where(c => c.DeptId == deptId.Value);

        var courses = await query
            .OrderBy(c => c.Department.Name).ThenBy(c => c.Code)
            .Select(c => new
            {
                c.CourseId,
                c.DeptId,
                DepartmentName = c.Department.Name,
                c.Name,
                c.Code,
                SectionCount = c.Sections.Count
            })
            .ToListAsync();
        return Ok(courses);
    }

    // GET /api/courses/{id}
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin,Invigilator")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var course = await _db.Courses
            .Where(c => c.CourseId == id)
            .Select(c => new
            {
                c.CourseId,
                c.DeptId,
                DepartmentName = c.Department.Name,
                c.Name,
                c.Code,
                SectionCount = c.Sections.Count,
                Sections = c.Sections.OrderBy(s => s.Name)
                    .Select(s => new { s.SectionId, s.Name, s.Semester, TeacherName = s.Teacher.Name })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        return course is null ? NotFound(new { message = "Course not found." }) : Ok(course);
    }

    // POST /api/courses {deptId, name, code}
    [HttpPost]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Create([FromBody] CourseRequest req)
    {
        var (fields, invalid) = await ValidateAsync(req, null);
        if (invalid is not null) return invalid;

        var course = new Course { DeptId = fields.DeptId, Name = fields.Name, Code = fields.Code };
        _db.Courses.Add(course);
        _audit.Add(CurrentUserId, "COURSE_CREATED", "Course", course.CourseId.ToString(),
            new { course.Code, course.Name, course.DeptId });
        if (!await TrySaveAsync()) return DuplicateCode(fields.Code);

        return CreatedAtAction(nameof(GetById), new { id = course.CourseId },
            new { course.CourseId, course.DeptId, course.Name, course.Code, SectionCount = 0 });
    }

    // PUT /api/courses/{id} {deptId, name, code} — may also move the course to another department
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CourseRequest req)
    {
        var course = await _db.Courses.FindAsync(id);
        if (course is null) return NotFound(new { message = "Course not found." });

        var (fields, invalid) = await ValidateAsync(req, id);
        if (invalid is not null) return invalid;

        var before = new { course.DeptId, course.Name, course.Code };
        course.DeptId = fields.DeptId;
        course.Name = fields.Name;
        course.Code = fields.Code;
        _audit.Add(CurrentUserId, "COURSE_UPDATED", "Course", id.ToString(),
            new { before, after = new { course.DeptId, course.Name, course.Code } });
        if (!await TrySaveAsync()) return DuplicateCode(fields.Code);

        return Ok(new { course.CourseId, course.DeptId, course.Name, course.Code });
    }

    // DELETE /api/courses/{id} — 409 while the course still has sections
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var course = await _db.Courses.FindAsync(id);
        if (course is null) return NotFound(new { message = "Course not found." });

        var sectionCount = await _db.Sections.CountAsync(s => s.CourseId == id);
        if (sectionCount > 0)
            return Conflict(new { code = "COURSE_HAS_SECTIONS", message = $"{course.Code} still has {sectionCount} section(s). Remove them first." });

        _db.Courses.Remove(course);
        _audit.Add(CurrentUserId, "COURSE_DELETED", "Course", id.ToString(), new { course.Code, course.Name, course.DeptId });
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Course codes are compared and stored trimmed and upper-case.</summary>
    public static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    private record CourseFields(Guid DeptId, string Name, string Code);

    private async Task<(CourseFields fields, IActionResult? error)> ValidateAsync(CourseRequest req, Guid? exceptId)
    {
        var name = req.Name?.Trim() ?? string.Empty;
        var code = NormalizeCode(req.Code);
        var fields = new CourseFields(req.DeptId ?? Guid.Empty, name, code);

        if (req.DeptId is null || req.DeptId == Guid.Empty)
            return (fields, BadRequest(new { message = "Department is required." }));
        if (name.Length == 0)
            return (fields, BadRequest(new { message = "Course name is required." }));
        if (name.Length > MaxNameLength)
            return (fields, BadRequest(new { message = $"Course name must be at most {MaxNameLength} characters." }));
        if (code.Length == 0)
            return (fields, BadRequest(new { message = "Course code is required." }));
        if (code.Length > MaxCodeLength)
            return (fields, BadRequest(new { message = $"Course code must be at most {MaxCodeLength} characters." }));
        if (!await _db.Departments.AnyAsync(d => d.DeptId == req.DeptId))
            return (fields, BadRequest(new { code = "UNKNOWN_DEPARTMENT", message = "Department not found." }));

        // Existing rows may predate normalisation, so compare upper-cased on both sides.
        if (await _db.Courses.AnyAsync(c => c.DeptId == req.DeptId && c.CourseId != exceptId && c.Code.ToUpper() == code))
            return (fields, DuplicateCode(code));

        return (fields, null);
    }

    private ObjectResult DuplicateCode(string code) =>
        Conflict(new { code = "DUPLICATE_COURSE_CODE", message = $"Course code {code} already exists in this department." });

    // False when the unique (DeptId, Code) index rejected the save: two concurrent requests both passed the check.
    private async Task<bool> TrySaveAsync()
    {
        try
        {
            await _db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }
}

public record CourseRequest(Guid? DeptId, string? Name, string? Code);
