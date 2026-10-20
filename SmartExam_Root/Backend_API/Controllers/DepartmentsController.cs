using Backend_API.Data;
using Backend_API.Models;
using Backend_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

/// <summary>
/// Module 7 — departments of the academic structure (Department → Course → Section).
/// Staff read them (course and section pickers); only Admin / SuperAdmin change them.
/// A department that still has courses cannot be deleted, so no course or section is orphaned.
/// </summary>
[ApiController]
[Route("api/departments")]
[Authorize]
public class DepartmentsController : ControllerBase
{
    public const int MaxNameLength = 128;

    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public DepartmentsController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);

    // GET /api/departments — every department with its course count
    [HttpGet]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin,Invigilator")]
    public async Task<IActionResult> GetAll()
    {
        var departments = await _db.Departments
            .OrderBy(d => d.Name)
            .Select(d => new { d.DeptId, d.Name, CourseCount = d.Courses.Count })
            .ToListAsync();
        return Ok(departments);
    }

    // GET /api/departments/{id} — one department with its courses
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin,Invigilator")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var department = await _db.Departments
            .Where(d => d.DeptId == id)
            .Select(d => new
            {
                d.DeptId,
                d.Name,
                CourseCount = d.Courses.Count,
                Courses = d.Courses
                    .OrderBy(c => c.Code)
                    .Select(c => new { c.CourseId, c.Name, c.Code, SectionCount = c.Sections.Count })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        return department is null ? NotFound(new { message = "Department not found." }) : Ok(department);
    }

    // POST /api/departments {name}
    [HttpPost]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Create([FromBody] DepartmentRequest req)
    {
        var (name, invalid) = await ValidateNameAsync(req.Name, null);
        if (invalid is not null) return invalid;

        var department = new Department { Name = name };
        _db.Departments.Add(department);
        _audit.Add(CurrentUserId, "DEPARTMENT_CREATED", "Department", department.DeptId.ToString(), new { department.Name });
        if (!await TrySaveAsync()) return DuplicateName(name);

        return CreatedAtAction(nameof(GetById), new { id = department.DeptId },
            new { department.DeptId, department.Name, CourseCount = 0 });
    }

    // PUT /api/departments/{id} {name}
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] DepartmentRequest req)
    {
        var department = await _db.Departments.FindAsync(id);
        if (department is null) return NotFound(new { message = "Department not found." });

        var (name, invalid) = await ValidateNameAsync(req.Name, id);
        if (invalid is not null) return invalid;

        var before = department.Name;
        department.Name = name;
        _audit.Add(CurrentUserId, "DEPARTMENT_UPDATED", "Department", id.ToString(), new { before, after = name });
        if (!await TrySaveAsync()) return DuplicateName(name);

        return Ok(new { department.DeptId, department.Name });
    }

    // DELETE /api/departments/{id} — 409 while the department still has courses
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var department = await _db.Departments.FindAsync(id);
        if (department is null) return NotFound(new { message = "Department not found." });

        var courseCount = await _db.Courses.CountAsync(c => c.DeptId == id);
        if (courseCount > 0)
            return Conflict(new { code = "DEPARTMENT_HAS_COURSES", message = $"{department.Name} still has {courseCount} course(s). Delete or move them first." });

        _db.Departments.Remove(department);
        _audit.Add(CurrentUserId, "DEPARTMENT_DELETED", "Department", id.ToString(), new { department.Name });
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // Trimmed name, or a 400/409 result. Uniqueness is case-insensitive ("computer science" = "Computer Science").
    private async Task<(string name, IActionResult? error)> ValidateNameAsync(string? raw, Guid? exceptId)
    {
        var name = raw?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return (name, BadRequest(new { message = "Department name is required." }));
        if (name.Length > MaxNameLength)
            return (name, BadRequest(new { message = $"Department name must be at most {MaxNameLength} characters." }));

        var lower = name.ToLower();
        if (await _db.Departments.AnyAsync(d => d.DeptId != exceptId && d.Name.ToLower() == lower))
            return (name, DuplicateName(name));
        return (name, null);
    }

    private ObjectResult DuplicateName(string name) =>
        Conflict(new { code = "DUPLICATE_DEPARTMENT", message = $"A department named \"{name}\" already exists." });

    // False when the unique name index rejected the save: two concurrent requests both passed the check.
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

public record DepartmentRequest(string? Name);
