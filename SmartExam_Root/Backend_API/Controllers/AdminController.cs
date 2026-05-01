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
public class AdminController(IAuthService authService, ISeedService seedService, AppDbContext dbContext) : ControllerBase
{
    private readonly IAuthService _authService = authService;
    private readonly ISeedService _seedService = seedService;
    private readonly AppDbContext _dbContext = dbContext;

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

        var teachers = await _dbContext.Users
            .Where(x => x.InstitutionId == institutionId && x.Role == SystemRole.Teacher)
            .OrderBy(x => x.Username)
            .Select(x => new UserListDto(x.Id, x.Username, x.Email, x.Role, x.IsActive, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<UserListDto>>.Ok(teachers));
    }

    [HttpGet("users/students")]
    public async Task<IActionResult> ListStudents(CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));

        var students = await _dbContext.Users
            .Where(x => x.InstitutionId == institutionId && x.Role == SystemRole.Student)
            .OrderBy(x => x.Username)
            .Select(x => new UserListDto(x.Id, x.Username, x.Email, x.Role, x.IsActive, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

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

    private bool TryGetInstitutionId(out Guid institutionId)
    {
        var claimValue = User.FindFirstValue("institutionId");
        return Guid.TryParse(claimValue, out institutionId);
    }
}
