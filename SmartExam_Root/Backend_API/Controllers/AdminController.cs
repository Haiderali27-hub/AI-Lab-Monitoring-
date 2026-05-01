using System.Security.Claims;
using Backend_API.Contracts;
using Backend_API.Data;
using Backend_API.Models;
using Backend_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = $"{nameof(SystemRole.OrganizationAdmin)},{nameof(SystemRole.SuperAdmin)}")]
public class AdminController(
    IAuthService authService,
    ISeedService seedService,
    IPasswordService passwordService,
    AppDbContext dbContext) : ControllerBase
{
    private readonly IAuthService _authService = authService;
    private readonly ISeedService _seedService = seedService;
    private readonly IPasswordService _passwordService = passwordService;
    private readonly AppDbContext _dbContext = dbContext;

    [HttpGet("organization")]
    public async Task<IActionResult> GetOrganization(CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));

        var institution = await _dbContext.Institutions.FirstOrDefaultAsync(
            x => x.Id == institutionId,
            cancellationToken);

        if (institution is null)
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Organization not found."));

        return Ok(ApiResponse<object>.Ok(new
        {
            id = institution.Id,
            name = institution.Name,
            contactEmail = institution.ContactEmail
        }));
    }

    // User creation

    [HttpPost("users/teachers")]
    public async Task<IActionResult> CreateTeacher(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));

        var result = await _authService.CreateTeacherAsync(institutionId, request, cancellationToken);
        return this.FromServiceResult(result, StatusCodes.Status201Created);
    }

    [HttpPost("users/students")]
    public async Task<IActionResult> CreateStudent(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));

        var result = await _authService.CreateStudentAsync(institutionId, request, cancellationToken);
        return this.FromServiceResult(result, StatusCodes.Status201Created);
    }

    // User listing

    [HttpGet("users/teachers")]
    public async Task<IActionResult> ListTeachers(CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));

        var teachers = await BuildUserListAsync(institutionId, SystemRole.Teacher, cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<UserListDto>>.Ok(teachers));
    }

    [HttpGet("users/students")]
    public async Task<IActionResult> ListStudents(CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));

        var students = await BuildUserListAsync(institutionId, SystemRole.Student, cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<UserListDto>>.Ok(students));
    }

    // User management

    [HttpPut("users/{userId:guid}/toggle-active")]
    public async Task<IActionResult> ToggleUserActive(Guid userId, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));

        var user = await _dbContext.Users.FirstOrDefaultAsync(
            x => x.Id == userId && x.InstitutionId == institutionId &&
                 (x.Role == SystemRole.Teacher || x.Role == SystemRole.Student),
            cancellationToken);

        if (user is null)
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "User not found."));

        user.IsActive = !user.IsActive;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<object>.Ok(new { user.Id, user.IsActive }));
    }

    [HttpDelete("users/{userId:guid}")]
    public async Task<IActionResult> DeleteUser(Guid userId, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));

        var user = await _dbContext.Users.FirstOrDefaultAsync(
            x => x.Id == userId && x.InstitutionId == institutionId &&
                 (x.Role == SystemRole.Teacher || x.Role == SystemRole.Student),
            cancellationToken);

        if (user is null)
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "User not found."));

        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<object>.Ok(new { }));
    }

    [HttpPut("users/{userId:guid}")]
    public async Task<IActionResult> UpdateUser(
        Guid userId,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));

        var user = await _dbContext.Users.FirstOrDefaultAsync(
            x => x.Id == userId && x.InstitutionId == institutionId &&
                 (x.Role == SystemRole.Teacher || x.Role == SystemRole.Student),
            cancellationToken);

        if (user is null)
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "User not found."));

        var username = request.Username.Trim();
        var email = request.Email.Trim().ToLowerInvariant();

        var duplicate = await _dbContext.Users.AnyAsync(
            x => x.Id != userId && x.InstitutionId == institutionId &&
                 (x.Username.ToLower() == username.ToLower() || x.Email.ToLower() == email),
            cancellationToken);

        if (duplicate)
            return Conflict(ApiResponse<object>.Fail("USER_EXISTS", "Username or email already in use."));

        // ── Role Transition Cascade ─────────────────────────────────────────
        if (request.NewRole.HasValue && request.NewRole.Value != user.Role)
        {
            var targetRole = request.NewRole.Value;

            // Only allow transitions between Student and Teacher
            if (targetRole != SystemRole.Student && targetRole != SystemRole.Teacher)
                return BadRequest(ApiResponse<object>.Fail("INVALID_ROLE",
                    "Admin users can only be assigned the Student or Teacher role."));

            if (user.Role == SystemRole.Student && targetRole == SystemRole.Teacher)
            {
                // Remove all section enrollments
                var enrollments = await _dbContext.SectionEnrollments
                    .Where(x => x.StudentUserId == userId)
                    .ToListAsync(cancellationToken);
                _dbContext.SectionEnrollments.RemoveRange(enrollments);

                // Remove from any exam assignments
                var examAssignments = await _dbContext.ExamAssignments
                    .Where(x => x.StudentUserId == userId)
                    .ToListAsync(cancellationToken);
                _dbContext.ExamAssignments.RemoveRange(examAssignments);
            }
            else if (user.Role == SystemRole.Teacher && targetRole == SystemRole.Student)
            {
                // Remove all teacher section assignments
                var teacherAssignments = await _dbContext.TeacherSectionAssignments
                    .Where(x => x.TeacherUserId == userId)
                    .ToListAsync(cancellationToken);
                _dbContext.TeacherSectionAssignments.RemoveRange(teacherAssignments);

                // Remove as proctor from exams (set to null)
                var proctored = await _dbContext.Exams
                    .Where(x => x.ProctorUserId == userId)
                    .ToListAsync(cancellationToken);
                foreach (var exam in proctored)
                    exam.ProctorUserId = null;
            }

            user.Role = targetRole;
        }

        user.Username = username;
        user.Email = email;
        user.IsActive = request.IsActive;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<object>.Ok(new
        {
            user.Id,
            user.Username,
            user.Email,
            user.IsActive,
            Role = user.Role.ToString()
        }));
    }

    [HttpPost("users/{userId:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(
        Guid userId,
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));

        var user = await _dbContext.Users.FirstOrDefaultAsync(
            x => x.Id == userId && x.InstitutionId == institutionId &&
                 (x.Role == SystemRole.Teacher || x.Role == SystemRole.Student),
            cancellationToken);

        if (user is null)
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "User not found."));

        user.PasswordHash = _passwordService.HashPassword(request.NewPassword);

        // Revoke all active sessions so the user must log in again with the new password
        var sessions = await _dbContext.UserSessions
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null && x.ExpiresAtUtc > DateTime.UtcNow)
            .ToListAsync(cancellationToken);
        foreach (var s in sessions)
            s.RevokedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { user.Id, PasswordReset = true }));
    }


    // Device binding management

    [HttpPost("students/batch-upload")]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> BatchUploadStudents(
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));

        if (file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("EMPTY_FILE", "CSV file is empty."));

        await using var stream = file.OpenReadStream();
        var result = await _authService.BatchCreateStudentsAsync(institutionId, stream, cancellationToken);
        return this.FromServiceResult(result);
    }

    [HttpGet("students/device-bindings")]
    public async Task<IActionResult> GetDeviceBindings(CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));

        var data = await _authService.GetStudentBindingStatusesAsync(institutionId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<StudentBindingStatusDto>>.Ok(data));
    }

    [HttpPost("students/{studentId:guid}/reset-binding")]
    public async Task<IActionResult> ResetBinding(Guid studentId, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));

        var result = await _authService.ResetStudentBindingAsync(institutionId, studentId, cancellationToken);
        return this.FromServiceResult(result);
    }

    [HttpPost("students/{studentId:guid}/force-logout")]
    public async Task<IActionResult> ForceLogout(Guid studentId, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));

        var result = await _authService.ForceTerminateStudentSessionsAsync(institutionId, studentId, cancellationToken);
        return this.FromServiceResult(result);
    }

    [HttpPost("seed-demo")]
    public async Task<IActionResult> SeedDemo([FromBody] SeedDemoRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));

        var result = await _seedService.SeedDemoAsync(institutionId, request, cancellationToken);
        return this.FromServiceResult(result);
    }

    [HttpGet("hierarchy/departments")]
    public async Task<IActionResult> ListDepartments(CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));

        var departments = await _dbContext.Departments
            .Where(x => x.InstitutionId == institutionId)
            .OrderBy(x => x.Name)
            .Select(x => new DepartmentDto(
                x.Id,
                x.Name,
                x.Code ?? string.Empty,
                x.IsActive,
                x.Sections.SelectMany(s => s.TeacherAssignments).Count(),
                x.Sections.SelectMany(s => s.Enrollments).Count(e => e.IsActive),
                x.Sections.Count))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<DepartmentDto>>.Ok(departments));
    }

    [HttpPost("hierarchy/departments")]
    public async Task<IActionResult> CreateDepartment([FromBody] CreateDepartmentRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));

        var code = request.Code.Trim().ToUpperInvariant();
        var exists = await _dbContext.Departments.AnyAsync(
            x => x.InstitutionId == institutionId && x.Code == code,
            cancellationToken);

        if (exists)
            return Conflict(ApiResponse<object>.Fail("DEPARTMENT_EXISTS", "A department with this code already exists."));

        var department = new Department
        {
            InstitutionId = institutionId,
            Name = request.Name.Trim(),
            Code = code
        };

        _dbContext.Departments.Add(department);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ApiResponse<DepartmentDto>.Ok(
            new DepartmentDto(department.Id, department.Name, department.Code, department.IsActive, 0, 0, 0)));
    }

    [HttpGet("hierarchy/sections")]
    public async Task<IActionResult> ListSections(CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));

        var sections = await _dbContext.Sections
            .Where(x => x.InstitutionId == institutionId)
            .OrderBy(x => x.Department.Name)
            .ThenBy(x => x.Name)
            .Select(x => new AcademicSectionDto(
                x.Id,
                x.DepartmentId,
                x.Department.Name,
                x.Name,
                x.Code ?? string.Empty,
                string.Empty,
                null,
                x.IsActive,
                x.Enrollments.Count(e => e.IsActive)))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<AcademicSectionDto>>.Ok(sections));
    }

    [HttpPost("hierarchy/sections")]
    public async Task<IActionResult> CreateSection([FromBody] CreateAcademicSectionRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));

        var department = await _dbContext.Departments.FirstOrDefaultAsync(
            x => x.Id == request.DepartmentId && x.InstitutionId == institutionId,
            cancellationToken);

        if (department is null)
            return BadRequest(ApiResponse<object>.Fail("DEPARTMENT_NOT_FOUND", "Selected department was not found."));

        var code = request.Code.Trim().ToUpperInvariant();
        var exists = await _dbContext.Sections.AnyAsync(
            x => x.DepartmentId == department.Id && x.Code == code,
            cancellationToken);

        if (exists)
            return Conflict(ApiResponse<object>.Fail("SECTION_EXISTS", "A section with this code already exists in this department."));

        var course = new Course
        {
            InstitutionId = institutionId,
            DepartmentId = department.Id,
            Name = request.Name.Trim(),
            Code = code
        };

        var section = new Section
        {
            InstitutionId = institutionId,
            DepartmentId = department.Id,
            Course = course,
            Name = request.Name.Trim(),
            Code = code,
        };

        _dbContext.Courses.Add(course);
        _dbContext.Sections.Add(section);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ApiResponse<AcademicSectionDto>.Ok(
            new AcademicSectionDto(
                section.Id,
                department.Id,
                department.Name,
                section.Name,
                section.Code ?? string.Empty,
                request.AcademicYear.Trim(),
                string.IsNullOrWhiteSpace(request.Semester) ? null : request.Semester.Trim(),
                section.IsActive,
                0)));
    }

    private async Task<IReadOnlyList<UserListDto>> BuildUserListAsync(
        Guid institutionId,
        SystemRole role,
        CancellationToken cancellationToken)
    {
        var users = await _dbContext.Users
            .Where(x => x.InstitutionId == institutionId && x.Role == role)
            .OrderBy(x => x.Username)
            .ToListAsync(cancellationToken);

        var userIds = users.Select(x => x.Id).ToList();

        var studentSections = role == SystemRole.Student
            ? await _dbContext.SectionEnrollments
                .Include(x => x.Section)
                .ThenInclude(x => x.Department)
                .Where(x => userIds.Contains(x.StudentUserId) && x.IsActive)
                .GroupBy(x => x.StudentUserId)
                .Select(x => x.OrderByDescending(e => e.EnrolledAtUtc).First())
                .ToDictionaryAsync(x => x.StudentUserId, cancellationToken)
            : new Dictionary<Guid, SectionEnrollment>();

        var teacherSections = role == SystemRole.Teacher
            ? await _dbContext.TeacherSectionAssignments
                .Include(x => x.Section)
                .ThenInclude(x => x.Department)
                .Where(x => userIds.Contains(x.TeacherUserId))
                .GroupBy(x => x.TeacherUserId)
                .Select(x => x.OrderByDescending(e => e.AssignedAtUtc).First())
                .ToDictionaryAsync(x => x.TeacherUserId, cancellationToken)
            : new Dictionary<Guid, TeacherSectionAssignment>();

        return users.Select(user =>
        {
            Section? section = null;
            if (role == SystemRole.Student && studentSections.TryGetValue(user.Id, out var enrollment))
            {
                section = enrollment.Section;
            }
            else if (role == SystemRole.Teacher && teacherSections.TryGetValue(user.Id, out var assignment))
            {
                section = assignment.Section;
            }

            return new UserListDto(
                user.Id,
                user.Username,
                user.Email,
                user.Role,
                user.IsActive,
                user.CreatedAtUtc,
                section?.DepartmentId,
                section?.Department.Name,
                section?.Id,
                section?.Name);
        }).ToList();
    }

    private bool TryGetInstitutionId(out Guid institutionId)
    {
        var claimValue = User.FindFirstValue("institutionId");
        return Guid.TryParse(claimValue, out institutionId);
    }
}
