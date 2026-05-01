using Backend_API.Contracts;
using Backend_API.Data;
using Backend_API.Models;
using Backend_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

[ApiController]
[Route("api/super-admin")]
public class SuperAdminController(IAuthService authService, AppDbContext dbContext, IPasswordService passwordService) : ControllerBase
{
    private readonly IAuthService _authService = authService;
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IPasswordService _passwordService = passwordService;

    [HttpPost("bootstrap")]
    public async Task<IActionResult> Bootstrap([FromBody] BootstrapAdminRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.BootstrapSuperAdminAsync(request, cancellationToken);
        return this.FromServiceResult(result);
    }

    [HttpPost("institutions")]
    [Authorize(Roles = nameof(SystemRole.SuperAdmin))]
    public async Task<IActionResult> CreateInstitution([FromBody] UpdateInstitutionRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var email = request.ContactEmail.Trim().ToLowerInvariant();
        var exists = await _dbContext.Institutions.AnyAsync(x => x.Name == name, cancellationToken);
        if (exists)
        {
            return Conflict(ApiResponse<object>.Fail("INSTITUTION_EXISTS", "An institution with this name already exists."));
        }

        var institution = new Institution
        {
            Name = name,
            ContactEmail = email,
            LogoUrl = request.LogoUrl,
            AllowedIpRanges = request.AllowedIpRanges,
            EnforceSingleDeviceBinding = request.EnforceSingleDeviceBinding,
            AllowTeacherResetBinding = request.AllowTeacherResetBinding,
            SessionTimeoutMinutes = request.SessionTimeoutMinutes
        };

        _dbContext.Institutions.Add(institution);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<Institution>.Ok(institution));
    }

    [HttpGet("institutions")]
    [Authorize(Roles = nameof(SystemRole.SuperAdmin))]
    public async Task<IActionResult> ListInstitutions(CancellationToken cancellationToken)
    {
        var institutions = await _dbContext.Institutions.ToListAsync(cancellationToken);
        return Ok(ApiResponse<List<Institution>>.Ok(institutions));
    }

    [HttpPost("institutions/{institutionId:guid}/admins")]
    [Authorize(Roles = nameof(SystemRole.SuperAdmin))]
    public async Task<IActionResult> CreateInstitutionAdmin(Guid institutionId, [FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        var institution = await _dbContext.Institutions.FindAsync(new object[] { institutionId }, cancellationToken);
        if (institution == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Institution not found."));

        var username = request.Username.Trim();
        var email = request.Email.Trim().ToLowerInvariant();
        var duplicate = await _dbContext.Users.AnyAsync(
            x => x.InstitutionId == institutionId &&
                 (x.Username.ToLower() == username.ToLower() || x.Email.ToLower() == email),
            cancellationToken);

        if (duplicate)
        {
            return Conflict(ApiResponse<object>.Fail("USER_EXISTS", "A user with this username or email already exists."));
        }

        var admin = new User
        {
            InstitutionId = institutionId,
            Username = username,
            Email = email,
            PasswordHash = _passwordService.HashPassword(request.Password),
            Role = SystemRole.OrganizationAdmin,
            IsActive = true
        };

        _dbContext.Users.Add(admin);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<object>.Ok(new { admin.Id, admin.Username, admin.Email }));
    }
}
