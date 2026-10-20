using Backend_API.Data;
using Backend_API.DTOs.Users;
using Backend_API.Helpers;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Backend_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly RefreshTokenService _refreshTokens;

    public UsersController(AppDbContext db, AuditService audit, RefreshTokenService refreshTokens)
    {
        _db = db;
        _audit = audit;
        _refreshTokens = refreshTokens;
    }

    private const int MaxRollNumberLength = 32;

    // Validates + normalises a requested roll number. Returns an error result, or null when acceptable.
    private async Task<IActionResult?> ValidateRollNumberAsync(string? normalized, Guid? exceptUserId)
    {
        if (normalized is null) return null;
        if (normalized.Length > MaxRollNumberLength)
            return BadRequest(new { message = $"Roll number must be at most {MaxRollNumberLength} characters." });
        if (normalized.Contains('@'))
            return BadRequest(new { message = "Roll number must not contain '@'." });
        if (await _db.Users.AnyAsync(u => u.RollNumber == normalized && u.UserId != exceptUserId))
            return Conflict(new { message = "Roll number already in use by another user." });
        return null;
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);

    // ── Module 7: SuperAdmin-only management of privileged accounts ──────────
    // An Admin manages students, teachers and invigilators. Creating, promoting to, editing, deleting,
    // (de)activating or signing out an Admin or SuperAdmin account needs a SuperAdmin, so an Admin can
    // never raise their own (or anyone's) privileges. Acting on one's own account is governed by the
    // existing self-checks (no own role change / delete / deactivate) instead.

    private static bool IsPrivileged(UserRole role) => role is UserRole.Admin or UserRole.SuperAdmin;

    private const string SuperAdminRequiredMessage = "Only a SuperAdmin can create or manage Admin and SuperAdmin accounts.";

    // Null when allowed; otherwise an audited 403. existingRole/requestedRole: the account's role now / after the change.
    private async Task<IActionResult?> GuardPrivilegedAsync(string action, Guid? targetId, UserRole? existingRole, UserRole? requestedRole)
    {
        if (User.IsInRole("SuperAdmin")) return null;
        if (targetId is not null && targetId == CurrentUserId) return null; // self: the endpoint's own checks apply
        var touchesPrivileged = (existingRole is { } e && IsPrivileged(e)) || (requestedRole is { } r && IsPrivileged(r));
        if (!touchesPrivileged) return null;

        await _audit.LogAsync(CurrentUserId, "PRIVILEGED_ACCOUNT_DENIED", "User", targetId?.ToString(),
            new { action, existingRole = existingRole?.ToString(), requestedRole = requestedRole?.ToString() });
        return StatusCode(StatusCodes.Status403Forbidden, new { code = "SUPERADMIN_REQUIRED", message = SuperAdminRequiredMessage });
    }

    // Same guard for actions on an existing account (delete, deactivate, force-logout, …).
    private async Task<IActionResult?> GuardExistingAsync(string action, Guid id)
    {
        var role = await _db.Users.Where(u => u.UserId == id).Select(u => (UserRole?)u.Role).FirstOrDefaultAsync();
        return role is null ? null : await GuardPrivilegedAsync(action, id, role, role);
    }

    // GET /api/users?role=Student
    [HttpGet]
    [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
    public async Task<IActionResult> GetAll([FromQuery] UserRole? role)
    {
        var query = _db.Users
            .Include(u => u.DeviceBinding)
            .AsQueryable();

        if (role.HasValue)
            query = query.Where(u => u.Role == role.Value);

        var users = await query
            .Select(u => new
            {
                u.UserId, u.Name, u.Email, u.RollNumber, u.Role, u.IsActive, u.CreatedAt,
                DeviceBound = u.DeviceBinding != null,
                DeviceRegisteredAt = u.DeviceBinding != null ? u.DeviceBinding.RegisteredAt : (DateTime?)null,
                MachineName = u.DeviceBinding != null ? u.DeviceBinding.MachineName : null,
                WindowsUsername = u.DeviceBinding != null ? u.DeviceBinding.WindowsUsername : null,
                DeviceLastSeenAt = u.DeviceBinding != null ? u.DeviceBinding.LastSeenAt : (DateTime?)null
            })
            .ToListAsync();

        return Ok(users);
    }

    // GET /api/users/{id}
    // Staff-only, and projected to a safe shape — never expose PasswordHash/Salt.
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await _db.Users
            .Include(u => u.DeviceBinding)
            .Where(u => u.UserId == id)
            .Select(u => new
            {
                u.UserId, u.Name, u.Email, u.RollNumber, u.Role, u.IsActive, u.CreatedAt,
                DeviceBound = u.DeviceBinding != null,
                DeviceRegisteredAt = u.DeviceBinding != null ? u.DeviceBinding.RegisteredAt : (DateTime?)null,
                MachineName = u.DeviceBinding != null ? u.DeviceBinding.MachineName : null,
                WindowsUsername = u.DeviceBinding != null ? u.DeviceBinding.WindowsUsername : null,
                DeviceLastSeenAt = u.DeviceBinding != null ? u.DeviceBinding.LastSeenAt : (DateTime?)null
            })
            .FirstOrDefaultAsync();

        if (user is null) return NotFound();
        return Ok(user);
    }

    // POST /api/users
    [HttpPost]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
    {
        if (!Enum.IsDefined(req.Role))
            return BadRequest(new { message = "Unknown role." });
        var denied = await GuardPrivilegedAsync("create", null, null, req.Role);
        if (denied is not null) return denied;

        if (await _db.Users.AnyAsync(u => u.Email == req.Email))
            return Conflict(new { message = "Email already in use." });

        var rollNumber = AuthController.NormalizeRollNumber(req.RollNumber);
        var rollError = await ValidateRollNumberAsync(rollNumber, null);
        if (rollError is not null) return rollError;

        var salt = PasswordHelper.GenerateSalt();
        var user = new User
        {
            Name = req.Name,
            Email = req.Email,
            RollNumber = rollNumber,
            Role = req.Role,
            Salt = salt,
            PasswordHash = PasswordHelper.HashPassword(req.Password, salt)
        };

        _db.Users.Add(user);
        _audit.Add(CurrentUserId, "USER_CREATED", "User", user.UserId.ToString(), new { user.Email, role = user.Role.ToString() });
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = user.UserId },
            new { user.UserId, user.Name, user.Email, user.RollNumber, user.Role });
    }

    // PUT /api/users/{id}  — Edit name/email/role, optionally reset password
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest req)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound(new { message = "User not found." });

        if (!Enum.IsDefined(req.Role))
            return BadRequest(new { message = "Unknown role." });
        var denied = await GuardPrivilegedAsync("update", id, user.Role, req.Role);
        if (denied is not null) return denied;

        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { message = "Name is required." });
        if (string.IsNullOrWhiteSpace(req.Email))
            return BadRequest(new { message = "Email is required." });

        // Email must stay unique across other users
        var emailTaken = await _db.Users.AnyAsync(u => u.Email == req.Email && u.UserId != id);
        if (emailTaken) return Conflict(new { message = "Email already in use by another user." });

        // Guard: a SuperAdmin must not be able to demote themselves out of admin access by accident
        if (id == CurrentUserId && user.Role != req.Role)
            return BadRequest(new { message = "You cannot change your own role." });

        // RollNumber: null = leave unchanged, "" = clear, otherwise set (unique).
        if (req.RollNumber is not null)
        {
            var rollNumber = AuthController.NormalizeRollNumber(req.RollNumber);
            var rollError = await ValidateRollNumberAsync(rollNumber, id);
            if (rollError is not null) return rollError;
            user.RollNumber = rollNumber;
        }

        user.Name = req.Name;
        user.Email = req.Email;
        user.Role = req.Role;

        var passwordReset = false;
        if (!string.IsNullOrWhiteSpace(req.Password))
        {
            if (req.Password.Length < 8)
                return BadRequest(new { message = "Password must be at least 8 characters." });
            user.Salt = PasswordHelper.GenerateSalt();
            user.PasswordHash = PasswordHelper.HashPassword(req.Password, user.Salt);
            passwordReset = true;
        }

        _audit.Add(CurrentUserId, "USER_UPDATED", "User", id.ToString(), new { user.Email, user.RollNumber, role = user.Role.ToString(), passwordReset });
        await _db.SaveChangesAsync();

        // An admin password reset signs the user out everywhere (refresh tokens + live access tokens),
        // except the admin's own current session when they reset their own password here.
        if (passwordReset)
        {
            var keepJti = id == CurrentUserId
                ? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value
                : null;
            await _refreshTokens.RevokeOtherSessionsAsync(id, keepJti, RefreshTokenRevocation.PasswordReset);
        }

        return Ok(new { user.UserId, user.Name, user.Email, user.RollNumber, user.Role, message = "User updated." });
    }

    // DELETE /api/users/{id}  — Hard delete, guarded to protect referential integrity
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound(new { message = "User not found." });
        var denied = await GuardExistingAsync("delete", id);
        if (denied is not null) return denied;

        if (id == CurrentUserId)
            return BadRequest(new { message = "You cannot delete your own account." });

        // A teacher who owns sections can't be removed without orphaning courses
        if (await _db.Sections.AnyAsync(s => s.TeacherId == id))
            return Conflict(new { message = "This user owns course sections. Reassign them before deleting, or deactivate instead." });

        // Preserve exam history: students with sessions should be deactivated, not deleted
        if (await _db.ExamSessions.AnyAsync(s => s.UserId == id))
            return Conflict(new { message = "This user has exam history. Deactivate instead of deleting to preserve records." });

        // Clean up dependent rows that would otherwise block the delete
        var bindings = await _db.DeviceBindings.Where(d => d.UserId == id).ToListAsync();
        _db.DeviceBindings.RemoveRange(bindings);
        var sessions = await _db.UserSessions.Where(s => s.UserId == id).ToListAsync();
        _db.UserSessions.RemoveRange(sessions);
        var refreshTokens = await _db.RefreshTokens.Where(t => t.UserId == id).ToListAsync();
        _db.RefreshTokens.RemoveRange(refreshTokens);
        var assignments = await _db.ExamAssignments.Where(a => a.UserId == id).ToListAsync();
        _db.ExamAssignments.RemoveRange(assignments);
        var enrollments = await _db.SectionEnrollments.Where(e => e.UserId == id).ToListAsync();
        _db.SectionEnrollments.RemoveRange(enrollments);
        var proctorRows = await _db.ExamProctors.Where(p => p.UserId == id).ToListAsync();
        _db.ExamProctors.RemoveRange(proctorRows);

        _db.Users.Remove(user);
        _audit.Add(CurrentUserId, "USER_DELETED", "User", id.ToString(), new { user.Email, role = user.Role.ToString() });
        await _db.SaveChangesAsync();

        return Ok(new { message = "User deleted." });
    }

    // POST /api/users/import  — Batch create (frontend parses a CSV into rows)
    [HttpPost("import")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Import([FromBody] ImportUsersRequest req)
    {
        if (req.Users is null || req.Users.Count == 0)
            return BadRequest(new { message = "No rows to import." });

        var existingEmails = (await _db.Users.Select(u => u.Email).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingRolls = (await _db.Users.Where(u => u.RollNumber != null).Select(u => u.RollNumber!).ToListAsync())
            .ToHashSet(StringComparer.Ordinal);
        var created = 0;
        var skipped = new List<object>();
        var seenInBatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var callerIsSuperAdmin = User.IsInRole("SuperAdmin");

        for (int i = 0; i < req.Users.Count; i++)
        {
            var row = req.Users[i];
            var line = i + 1;
            if (string.IsNullOrWhiteSpace(row.Name) || string.IsNullOrWhiteSpace(row.Email) || string.IsNullOrWhiteSpace(row.Password))
            { skipped.Add(new { line, email = row.Email, reason = "missing name/email/password" }); continue; }
            if (!Enum.IsDefined(row.Role))
            { skipped.Add(new { line, email = row.Email, reason = "unknown role" }); continue; }
            if (IsPrivileged(row.Role) && !callerIsSuperAdmin)
            { skipped.Add(new { line, email = row.Email, reason = "only a SuperAdmin can create Admin/SuperAdmin accounts" }); continue; }
            if (existingEmails.Contains(row.Email) || !seenInBatch.Add(row.Email))
            { skipped.Add(new { line, email = row.Email, reason = "duplicate email" }); continue; }
            var rollNumber = AuthController.NormalizeRollNumber(row.RollNumber);
            if (rollNumber is not null && (rollNumber.Length > MaxRollNumberLength || rollNumber.Contains('@')))
            { skipped.Add(new { line, email = row.Email, reason = "invalid roll number" }); continue; }
            if (rollNumber is not null && !existingRolls.Add(rollNumber))
            { skipped.Add(new { line, email = row.Email, reason = "duplicate roll number" }); continue; }

            var salt = PasswordHelper.GenerateSalt();
            _db.Users.Add(new User
            {
                Name = row.Name.Trim(),
                Email = row.Email.Trim(),
                RollNumber = rollNumber,
                Role = row.Role,
                Salt = salt,
                PasswordHash = PasswordHelper.HashPassword(row.Password, salt)
            });
            created++;
        }

        _audit.Add(CurrentUserId, "USERS_IMPORTED", "User", null, new { created, skipped = skipped.Count });
        await _db.SaveChangesAsync();

        return Ok(new { created, skippedCount = skipped.Count, skipped });
    }

    // DELETE /api/users/{id}/device-binding  — Reset device binding
    [HttpDelete("{id:guid}/device-binding")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> ResetDeviceBinding(Guid id)
    {
        var denied = await GuardExistingAsync("reset-device-binding", id);
        if (denied is not null) return denied;
        var binding = await _db.DeviceBindings.FirstOrDefaultAsync(d => d.UserId == id);
        if (binding is null) return NotFound(new { message = "No device binding found." });

        _db.DeviceBindings.Remove(binding);
        _audit.Add(CurrentUserId, "DEVICE_BINDING_RESET", "DeviceBinding", id.ToString());
        await _db.SaveChangesAsync();

        return Ok(new { message = "Device binding reset. Student can re-register on next login." });
    }

    // POST /api/users/{id}/force-logout  — Revoke all active sessions
    [HttpPost("{id:guid}/force-logout")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> ForceLogout(Guid id)
    {
        var denied = await GuardExistingAsync("force-logout", id);
        if (denied is not null) return denied;
        // Refresh tokens first: otherwise the client would simply mint a new access token.
        var revokedRefreshTokens = await _refreshTokens.RevokeAllForUserAsync(id, RefreshTokenRevocation.ForceLogout);

        var sessions = await _db.UserSessions
            .Where(s => s.UserId == id && !s.IsRevoked)
            .ToListAsync();

        sessions.ForEach(s => s.IsRevoked = true);
        _audit.Add(CurrentUserId, "FORCE_LOGOUT", "User", id.ToString(), new { revokedCount = sessions.Count, revokedRefreshTokens });
        await _db.SaveChangesAsync();

        return Ok(new { message = $"Revoked {sessions.Count} active session(s)." });
    }

    // PATCH /api/users/{id}/deactivate
    [HttpPatch("{id:guid}/deactivate")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();
        var denied = await GuardExistingAsync("deactivate", id);
        if (denied is not null) return denied;

        if (id == CurrentUserId)
            return BadRequest(new { message = "You cannot deactivate your own account." });

        await _refreshTokens.RevokeAllForUserAsync(id, RefreshTokenRevocation.Deactivated);
        user = await _db.Users.FindAsync(id); // the revocation may have cleared the change tracker
        if (user is null) return NotFound();

        user.IsActive = false;
        // Revoke live sessions so deactivation takes effect immediately
        var sessions = await _db.UserSessions.Where(s => s.UserId == id && !s.IsRevoked).ToListAsync();
        sessions.ForEach(s => s.IsRevoked = true);
        _audit.Add(CurrentUserId, "USER_DEACTIVATED", "User", id.ToString());
        await _db.SaveChangesAsync();

        return Ok(new { message = "User deactivated." });
    }

    // PATCH /api/users/{id}/activate  — Reactivate a deactivated account
    [HttpPatch("{id:guid}/activate")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Activate(Guid id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();
        var denied = await GuardExistingAsync("activate", id);
        if (denied is not null) return denied;

        user.IsActive = true;
        _audit.Add(CurrentUserId, "USER_ACTIVATED", "User", id.ToString());
        await _db.SaveChangesAsync();

        return Ok(new { message = "User activated." });
    }
}
