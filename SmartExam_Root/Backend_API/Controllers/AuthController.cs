using Backend_API.Data;
using Backend_API.DTOs.Auth;
using Backend_API.DTOs;
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

        bool deviceBound = user.DeviceBinding != null;

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

    // GET /api/health
    [HttpGet("/api/health")]
    [AllowAnonymous]
    public IActionResult Health()
    {
        return Ok(new { status = "Healthy" });
    }

    // POST /api/auth/student-login
    [HttpPost("student-login")]
    [AllowAnonymous]
    public async Task<IActionResult> StudentLogin([FromBody] StudentLoginRequest req)
    {
        var user = await _db.Users
            .Include(u => u.DeviceBinding)
            .FirstOrDefaultAsync(u => (u.Email == req.UsernameOrEmail || u.Name == req.UsernameOrEmail) && u.IsActive);

        if (user is null)
            return Unauthorized(new ApiEnvelope<object>(false, "UNAUTHORIZED", "Invalid username/email or password.", null));

        if (!PasswordHelper.VerifyPassword(req.Password, user.Salt, user.PasswordHash))
            return Unauthorized(new ApiEnvelope<object>(false, "UNAUTHORIZED", "Invalid username/email or password.", null));

        if (user.Role != UserRole.Student)
            return Unauthorized(new ApiEnvelope<object>(false, "FORBIDDEN", "Only students can log in via this client.", null));

        bool deviceBound = false;
        if (string.IsNullOrEmpty(req.HardwareFingerprint))
            return BadRequest(new ApiEnvelope<object>(false, "BAD_REQUEST", "HWID is required for student login.", null));

        if (user.DeviceBinding is null)
        {
            _db.DeviceBindings.Add(new DeviceBinding
            {
                UserId = user.UserId,
                HwidHash = req.HardwareFingerprint
            });
            deviceBound = true;
        }
        else
        {
            if (user.DeviceBinding.HwidHash != req.HardwareFingerprint)
                return Unauthorized(new ApiEnvelope<object>(false, "DEVICE_MISMATCH", "This account is bound to a different device. Contact your admin.", null));

            user.DeviceBinding.LastSeenAt = DateTime.UtcNow;
            deviceBound = true;
        }

        var (token, jti, expiry) = _jwt.GenerateToken(user);

        _db.UserSessions.Add(new UserSession
        {
            UserId = user.UserId,
            Jti = jti,
            ExpiresAt = expiry
        });

        await _db.SaveChangesAsync();

        var response = new TokenResponse(
            token,
            "dummy-refresh-token",
            expiry,
            new UserSummary(user.UserId, Guid.Empty, user.Name, user.Email, "Student"),
            deviceBound
        );

        return Ok(new ApiEnvelope<TokenResponse>(true, "SUCCESS", "Success", response));
    }

    // POST /api/auth/refresh
    [HttpPost("refresh")]
    [AllowAnonymous]
    public IActionResult Refresh([FromBody] RefreshRequest req)
    {
        return Ok(new ApiEnvelope<object>(true, "SUCCESS", "Success", null));
    }
}
