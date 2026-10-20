using Backend_API.Data;
using Backend_API.DTOs.Auth;
using Backend_API.DTOs;
using Backend_API.Helpers;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Backend_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtHelper _jwt;
    private readonly AuditService _audit;
    private readonly IConfiguration _config;

    public AuthController(AppDbContext db, JwtHelper jwt, AuditService audit, IConfiguration config)
    {
        _db = db;
        _jwt = jwt;
        _audit = audit;
        _config = config;
    }

    // POST /api/auth/login
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await _db.Users
            .Include(u => u.DeviceBinding)
            .FirstOrDefaultAsync(u => u.Email == req.Email);

        if (user is null || !PasswordHelper.VerifyPassword(req.Password, user.Salt, user.PasswordHash))
        {
            await _audit.LogAsync(user?.UserId, "LOGIN_FAILED", "User", user?.UserId.ToString(),
                new { email = req.Email, reason = user is null ? "unknown_email" : "bad_password" });
            return Unauthorized(new { message = "Invalid email or password." });
        }

        if (!user.IsActive)
        {
            await _audit.LogAsync(user.UserId, "LOGIN_DENIED_INACTIVE", "User", user.UserId.ToString(), new { email = req.Email });
            return Unauthorized(new { message = "This account has been deactivated. Contact your administrator." });
        }

        UpgradePasswordHashIfNeeded(user, req.Password);

        bool deviceBound = user.DeviceBinding != null;

        // Issue JWT
        var (token, jti, expiry) = _jwt.GenerateToken(user);

        _db.UserSessions.Add(new UserSession
        {
            UserId = user.UserId,
            Jti = jti,
            ExpiresAt = expiry
        });
        _audit.Add(user.UserId, "LOGIN_SUCCESS", "User", user.UserId.ToString(), new { user.Email, role = user.Role.ToString() });

        await _db.SaveChangesAsync();

        return Ok(new LoginResponse(token, user.UserId, user.Name, user.Email, user.Role, deviceBound));
    }

    // POST /api/auth/change-password
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return NotFound(new { message = "User not found." });

        if (!PasswordHelper.VerifyPassword(req.CurrentPassword, user.Salt, user.PasswordHash))
        {
            await _audit.LogAsync(userId, "PASSWORD_CHANGE_FAILED", "User", userId.ToString(), new { reason = "wrong_current_password" });
            return BadRequest(new { message = "Current password is incorrect." });
        }
        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 8)
            return BadRequest(new { message = "New password must be at least 8 characters." });

        user.Salt = PasswordHelper.GenerateSalt();
        user.PasswordHash = PasswordHelper.HashPassword(req.NewPassword, user.Salt);
        _audit.Add(userId, "PASSWORD_CHANGED", "User", userId.ToString());
        await _db.SaveChangesAsync();

        return Ok(new { message = "Password updated successfully." });
    }

    // POST /api/auth/logout
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var jti = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
        if (jti is null) return BadRequest();

        var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);
        var session = await _db.UserSessions.FirstOrDefaultAsync(s => s.Jti == jti);
        if (session is not null)
        {
            session.IsRevoked = true;
            _audit.Add(userId, "LOGOUT", "User", userId.ToString());
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
    [EnableRateLimiting("login")]
    public async Task<IActionResult> StudentLogin([FromBody] StudentLoginRequest req)
    {
        var user = await _db.Users
            .Include(u => u.DeviceBinding)
            .FirstOrDefaultAsync(u => (u.Email == req.UsernameOrEmail || u.Name == req.UsernameOrEmail) && u.IsActive);

        if (user is null || !PasswordHelper.VerifyPassword(req.Password, user.Salt, user.PasswordHash))
        {
            await _audit.LogAsync(user?.UserId, "LOGIN_FAILED", "User", user?.UserId.ToString(),
                new { identifier = req.UsernameOrEmail, channel = "student-client" });
            return Unauthorized(new ApiEnvelope<object>(false, "UNAUTHORIZED", "Invalid username/email or password.", null));
        }

        if (user.Role != UserRole.Student)
            return Unauthorized(new ApiEnvelope<object>(false, "FORBIDDEN", "Only students can log in via this client.", null));

        UpgradePasswordHashIfNeeded(user, req.Password);

        bool deviceBound = false;
        if (string.IsNullOrEmpty(req.HardwareFingerprint))
            return BadRequest(new ApiEnvelope<object>(false, "BAD_REQUEST", "HWID is required for student login.", null));

        // Store/compare the HASH of the raw fingerprint — the DB never holds raw device IDs.
        var hwidHash = HwidHelper.Hash(req.HardwareFingerprint);

        if (user.DeviceBinding is null)
        {
            _db.DeviceBindings.Add(new DeviceBinding
            {
                UserId = user.UserId,
                HwidHash = hwidHash,
                MachineName = req.MachineName,
                WindowsUsername = req.WindowsUsername
            });
            _audit.Add(user.UserId, "DEVICE_BOUND", "DeviceBinding", user.UserId.ToString(),
                new { firstRegistration = true, machineName = req.MachineName, windowsUsername = req.WindowsUsername });
            deviceBound = true;
        }
        else
        {
            if (user.DeviceBinding.HwidHash != hwidHash)
            {
                await _audit.LogAsync(user.UserId, "DEVICE_MISMATCH", "DeviceBinding", user.UserId.ToString(),
                    new { email = user.Email, outcome = "access_blocked", attemptedFromMachine = req.MachineName, attemptedByWindowsUser = req.WindowsUsername });
                return Unauthorized(new ApiEnvelope<object>(false, "DEVICE_MISMATCH", "This account is bound to a different device. Contact your admin.", null));
            }

            user.DeviceBinding.LastSeenAt = DateTime.UtcNow;
            // Keep the human-readable machine identity fresh on every login
            if (!string.IsNullOrWhiteSpace(req.MachineName)) user.DeviceBinding.MachineName = req.MachineName;
            if (!string.IsNullOrWhiteSpace(req.WindowsUsername)) user.DeviceBinding.WindowsUsername = req.WindowsUsername;
            deviceBound = true;
        }

        // Demo convenience only (Labs:AutoRegister=true): auto-register this machine as a workstation
        // in the Systems Engineering Lab. Off by default — letting any PC enrol itself would defeat
        // seat enforcement, so admins register lab PCs on the Labs page instead.
        if (_config.GetValue("Labs:AutoRegister", false))
            await EnsureWorkstationRegisteredAsync(req.MachineName);

        // The desktop token carries the machine identity reported at login; start-session, student/start
        // and every heartbeat compare it with the assigned seat (Module 9, Services/SeatGuard).
        var clientClaims = new List<System.Security.Claims.Claim> { new(SeatGuard.ClientClaim, SeatGuard.DesktopClient) };
        if (!string.IsNullOrWhiteSpace(req.MachineName))
            clientClaims.Add(new(SeatGuard.MachineClaim, req.MachineName.Trim()[..Math.Min(req.MachineName.Trim().Length, 128)]));

        var (token, jti, expiry) = _jwt.GenerateToken(user, clientClaims);
        _audit.Add(user.UserId, "LOGIN_SUCCESS", "User", user.UserId.ToString(), new { user.Email, role = "Student", channel = "student-client" });

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

    // Transparently migrate legacy SHA-256 hashes to PBKDF2 once we know the plaintext is correct.
    // Persisted by the SaveChangesAsync that follows in the login flow.
    private static void UpgradePasswordHashIfNeeded(User user, string password)
    {
        if (!PasswordHelper.NeedsRehash(user.PasswordHash)) return;
        user.Salt = PasswordHelper.GenerateSalt();
        user.PasswordHash = PasswordHelper.HashPassword(password, user.Salt);
    }

    // Idempotently add the caller's machine to the Systems Engineering Lab (only when Labs:AutoRegister=true).
    private async Task EnsureWorkstationRegisteredAsync(string? machineName)
    {
        if (string.IsNullOrWhiteSpace(machineName)) return;

        var lab = await _db.Labs.FirstOrDefaultAsync(l => l.Name == "Systems Engineering Lab");
        if (lab is null) return; // lab not seeded — skip silently

        var exists = await _db.Workstations.AnyAsync(w => w.LabId == lab.LabId && w.MachineNumber == machineName);
        if (exists) return;

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        _db.Workstations.Add(new Backend_API.Models.Workstation
        {
            LabId = lab.LabId,
            MachineNumber = machineName,
            IpAddress = ip
        });
        await _db.SaveChangesAsync();
    }
}
