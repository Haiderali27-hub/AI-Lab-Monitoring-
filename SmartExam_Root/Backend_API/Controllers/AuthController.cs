using Backend_API.Data;
using Backend_API.DTOs.Auth;
using Backend_API.Helpers;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtHelper _jwt;

    public AuthController(AppDbContext db, JwtHelper jwt)
    {
        _db = db;
        _jwt = jwt;
    }

    // POST /api/auth/login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await _db.Users
            .Include(u => u.DeviceBinding)
            .FirstOrDefaultAsync(u => u.Email == req.Email && u.IsActive);

        if (user is null)
            return Unauthorized(new { message = "Invalid email or password." });

        if (!PasswordHelper.VerifyPassword(req.Password, user.Salt, user.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password." });

        // Device binding check — only for students
        bool deviceBound = false;
        if (user.Role == UserRole.Student)
        {
            if (string.IsNullOrEmpty(req.HwidHash))
                return BadRequest(new { message = "HWID is required for student login." });

            if (user.DeviceBinding is null)
            {
                // First login — register this device
                _db.DeviceBindings.Add(new DeviceBinding
                {
                    UserId = user.UserId,
                    HwidHash = req.HwidHash
                });
                deviceBound = true;
            }
            else
            {
                // Subsequent logins — verify device
                if (user.DeviceBinding.HwidHash != req.HwidHash)
                    return Unauthorized(new { message = "This account is bound to a different device. Contact your admin." });

                user.DeviceBinding.LastSeenAt = DateTime.UtcNow;
                deviceBound = true;
            }
        }

        // Issue JWT
        var (token, jti, expiry) = _jwt.GenerateToken(user);

        _db.UserSessions.Add(new UserSession
        {
            UserId = user.UserId,
            Jti = jti,
            ExpiresAt = expiry
        });

        await _db.SaveChangesAsync();

        return Ok(new LoginResponse(token, user.UserId, user.Name, user.Email, user.Role, deviceBound));
    }

    // POST /api/auth/logout
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var jti = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
        if (jti is null) return BadRequest();

        var session = await _db.UserSessions.FirstOrDefaultAsync(s => s.Jti == jti);
        if (session is not null)
        {
            session.IsRevoked = true;
            await _db.SaveChangesAsync();
        }

        return Ok(new { message = "Logged out successfully." });
    }

    // GET /api/auth/me
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return NotFound();

        return Ok(new { user.UserId, user.Name, user.Email, user.Role });
    }
}
