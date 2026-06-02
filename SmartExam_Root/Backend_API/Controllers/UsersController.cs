using Backend_API.Data;
using Backend_API.DTOs.Users;
using Backend_API.Helpers;
using Backend_API.Models;
using Backend_API.Models.Enums;
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

    public UsersController(AppDbContext db) => _db = db;

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
                DeviceRegisteredAt = u.DeviceBinding != null ? u.DeviceBinding.RegisteredAt : (DateTime?)null
            })
            .ToListAsync();

        return Ok(users);
    }

    // GET /api/users/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await _db.Users
            .Include(u => u.DeviceBinding)
            .FirstOrDefaultAsync(u => u.UserId == id);

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
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = user.UserId },
            new { user.UserId, user.Name, user.Email, user.Role });
    }

    // DELETE /api/users/{id}/device-binding  — Reset device binding
    [HttpDelete("{id:guid}/device-binding")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> ResetDeviceBinding(Guid id)
    {
        var binding = await _db.DeviceBindings.FirstOrDefaultAsync(d => d.UserId == id);
        if (binding is null) return NotFound(new { message = "No device binding found." });

        _db.DeviceBindings.Remove(binding);
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

        user.IsActive = false;
        await _db.SaveChangesAsync();

        return Ok(new { message = "User deactivated." });
    }
}
