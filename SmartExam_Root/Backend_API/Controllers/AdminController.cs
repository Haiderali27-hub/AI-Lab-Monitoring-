using System.Security.Claims;
using Backend_API.Contracts;
using Backend_API.Models;
using Backend_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend_API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = nameof(SystemRole.OrganizationAdmin))]
public class AdminController(IAuthService authService, ISeedService seedService) : ControllerBase
{
    private readonly IAuthService _authService = authService;
    private readonly ISeedService _seedService = seedService;

    [HttpPost("users/teachers")]
    public async Task<IActionResult> CreateTeacher(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));
        }

        var result = await _authService.CreateTeacherAsync(institutionId, request, cancellationToken);
        return this.FromServiceResult(result, StatusCodes.Status201Created);
    }

    [HttpPost("users/students")]
    public async Task<IActionResult> CreateStudent(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));
        }

        var result = await _authService.CreateStudentAsync(institutionId, request, cancellationToken);
        return this.FromServiceResult(result, StatusCodes.Status201Created);
    }

    [HttpPost("students/batch-upload")]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> BatchUploadStudents(
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));
        }

        if (file.Length == 0)
        {
            return BadRequest(ApiResponse<object>.Fail("EMPTY_FILE", "CSV file is empty."));
        }

        await using var stream = file.OpenReadStream();
        var result = await _authService.BatchCreateStudentsAsync(institutionId, stream, cancellationToken);
        return this.FromServiceResult(result);
    }

    [HttpGet("students/device-bindings")]
    public async Task<IActionResult> GetDeviceBindings(CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));
        }

        var data = await _authService.GetStudentBindingStatusesAsync(institutionId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<StudentBindingStatusDto>>.Ok(data));
    }

    [HttpPost("students/{studentId:guid}/reset-binding")]
    public async Task<IActionResult> ResetBinding(Guid studentId, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));
        }

        var result = await _authService.ResetStudentBindingAsync(institutionId, studentId, cancellationToken);
        return this.FromServiceResult(result);
    }

    [HttpPost("students/{studentId:guid}/force-logout")]
    public async Task<IActionResult> ForceLogout(Guid studentId, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));
        }

        var result = await _authService.ForceTerminateStudentSessionsAsync(institutionId, studentId, cancellationToken);
        return this.FromServiceResult(result);
    }

    [HttpPost("seed-demo")]
    public async Task<IActionResult> SeedDemo([FromBody] SeedDemoRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetInstitutionId(out var institutionId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Institution claim missing."));
        }

        var result = await _seedService.SeedDemoAsync(institutionId, request, cancellationToken);
        return this.FromServiceResult(result);
    }

    private bool TryGetInstitutionId(out Guid institutionId)
    {
        var claimValue = User.FindFirstValue("institutionId");
        return Guid.TryParse(claimValue, out institutionId);
    }
}