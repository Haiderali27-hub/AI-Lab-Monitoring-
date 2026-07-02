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

    public UsersController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);

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
                u.UserId, u.Name, u.Email, u.Role, u.IsActive, u.CreatedAt,
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
                u.UserId, u.Name, u.Email, u.Role, u.IsActive, u.CreatedAt,
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
        if (await _db.Users.AnyAsync(u => u.Email == req.Email))
            return Conflict(new { message = "Email already in use." });

        var salt = PasswordHelper.GenerateSalt();
        var user = new User
        {
            Name = req.Name,
            Email = req.Email,
            Role = req.Role,
            Salt = salt,
            PasswordHash = PasswordHelper.HashPassword(req.Password, salt)
        };

        _db.Users.Add(user);
        _audit.Add(CurrentUserId, "USER_CREATED", "User", user.UserId.ToString(), new { user.Email, role = user.Role.ToString() });
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = user.UserId },
            new { user.UserId, user.Name, user.Email, user.Role });
    }

    // PUT /api/users/{id}  — Edit name/email/role, optionally reset password
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest req)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound(new { message = "User not found." });

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

        _audit.Add(CurrentUserId, "USER_UPDATED", "User", id.ToString(), new { user.Email, role = user.Role.ToString(), passwordReset });
        await _db.SaveChangesAsync();

        return Ok(new { user.UserId, user.Name, user.Email, user.Role, message = "User updated." });
    }

    // DELETE /api/users/{id}  — Hard delete, guarded to protect referential integrity
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound(new { message = "User not found." });

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
        var assignments = await _db.ExamAssignments.Where(a => a.UserId == id).ToListAsync();
        _db.ExamAssignments.RemoveRange(assignments);
        var enrollments = await _db.SectionEnrollments.Where(e => e.UserId == id).ToListAsync();
        _db.SectionEnrollments.RemoveRange(enrollments);

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
        var created = 0;
        var skipped = new List<object>();
        var seenInBatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < req.Users.Count; i++)
        {
            var row = req.Users[i];
            var line = i + 1;
            if (string.IsNullOrWhiteSpace(row.Name) || string.IsNullOrWhiteSpace(row.Email) || string.IsNullOrWhiteSpace(row.Password))
            { skipped.Add(new { line, email = row.Email, reason = "missing name/email/password" }); continue; }
            if (existingEmails.Contains(row.Email) || !seenInBatch.Add(row.Email))
            { skipped.Add(new { line, email = row.Email, reason = "duplicate email" }); continue; }

            var salt = PasswordHelper.GenerateSalt();
            _db.Users.Add(new User
            {
                Name = row.Name.Trim(),
                Email = row.Email.Trim(),
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
        var sessions = await _db.UserSessions
            .Where(s => s.UserId == id && !s.IsRevoked)
            .ToListAsync();

        sessions.ForEach(s => s.IsRevoked = true);
        _audit.Add(CurrentUserId, "FORCE_LOGOUT", "User", id.ToString(), new { revokedCount = sessions.Count });
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

        if (id == CurrentUserId)
            return BadRequest(new { message = "You cannot deactivate your own account." });

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

        user.IsActive = true;
        _audit.Add(CurrentUserId, "USER_ACTIVATED", "User", id.ToString());
        await _db.SaveChangesAsync();

        return Ok(new { message = "User activated." });
    }
}
