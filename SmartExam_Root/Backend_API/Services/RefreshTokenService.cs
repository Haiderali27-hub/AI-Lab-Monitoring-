using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Backend_API.Data;
using Backend_API.DTOs;
using Backend_API.Helpers;
using Backend_API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Backend_API.Services;

/// <summary>Result of POST /api/auth/refresh. <see cref="Tokens"/> is set only when <see cref="Ok"/>.</summary>
public sealed record RefreshOutcome(bool Ok, string Code, string Message, TokenResponse? Tokens)
{
    public static RefreshOutcome Fail(string code, string message) => new(false, code, message, null);
}

/// <summary>
/// Module 1: rotating refresh tokens.
///  • Login issues a 64-byte random token (Base64Url) in a new family; only its SHA-256 hash is stored.
///  • Refresh revokes the presented token ("rotated") and issues a new access token + a successor in
///    the same family. The family's absolute expiry is fixed at login — rotation never extends it.
///  • Presenting a rotated token again is a theft signal: every token of the family is revoked, and so
///    is every access token (UserSession) the family issued, so OnTokenValidated rejects them at once.
///  • Logout revokes the caller's family; force-logout / deactivation revoke all of the user's tokens.
/// </summary>
public class RefreshTokenService
{
    public const string InvalidCode = "INVALID_REFRESH_TOKEN";
    public const string ExpiredCode = "REFRESH_TOKEN_EXPIRED";
    public const string ReusedCode = "REFRESH_TOKEN_REUSED";

    private const int RawTokenBytes = 64;
    private const int MaxConcurrencyRetries = 3;
    // Long-expired rows are pruned when the same user logs in again.
    private static readonly TimeSpan PruneAfter = TimeSpan.FromDays(7);

    private readonly AppDbContext _db;
    private readonly JwtHelper _jwt;
    private readonly AuditService _audit;
    private readonly IConfiguration _config;

    public RefreshTokenService(AppDbContext db, JwtHelper jwt, AuditService audit, IConfiguration config)
    {
        _db = db;
        _jwt = jwt;
        _audit = audit;
        _config = config;
    }

    /// <summary>SHA-256 of the raw token as 64 lowercase hex characters (what the database stores).</summary>
    public static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken.Trim()))).ToLowerInvariant();

    public static string NewRawToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(RawTokenBytes));

    /// <summary>Absolute family lifetime: a lab day for the desktop client, a working day for staff.</summary>
    public TimeSpan LifetimeFor(string channel)
    {
        var key = channel == RefreshTokenChannels.StudentClient ? "Jwt:StudentRefreshHours" : "Jwt:StaffRefreshHours";
        return TimeSpan.FromHours(
            double.TryParse(_config[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var h) && h > 0 ? h : 12);
    }

    /// <summary>
    /// Stages a refresh token for a fresh login (new family). The caller's SaveChanges persists it.
    /// Returns the raw token, which is never stored.
    /// </summary>
    public async Task<string> IssueForLoginAsync(User user, string jti, string channel, string? machineName, string? ip)
    {
        var cutoff = DateTime.UtcNow - PruneAfter;
        var stale = await _db.RefreshTokens.Where(t => t.UserId == user.UserId && t.ExpiresAt < cutoff).ToListAsync();
        _db.RefreshTokens.RemoveRange(stale);

        var (raw, _) = Stage(user.UserId, jti, channel, machineName, ip, Guid.NewGuid(), DateTime.UtcNow + LifetimeFor(channel));
        return raw;
    }

    public async Task<RefreshOutcome> RotateAsync(string? rawToken, string? ip)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return RefreshOutcome.Fail(InvalidCode, "Refresh token is invalid.");

        var hash = Hash(rawToken);
        var row = await _db.RefreshTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (row is null)
            return RefreshOutcome.Fail(InvalidCode, "Refresh token is invalid.");

        var now = DateTime.UtcNow;
        if (row.RevokedAt is not null)
        {
            if (row.RevokedReason == RefreshTokenRevocation.Rotated)
            {
                await HandleReuseAsync(row.UserId, row.FamilyId, row.Channel, ip);
                return RefreshOutcome.Fail(ReusedCode, "Refresh token was already used. Please log in again.");
            }
            return RefreshOutcome.Fail(InvalidCode, "Refresh token has been revoked.");
        }

        if (row.ExpiresAt <= now)
            return RefreshOutcome.Fail(ExpiredCode, "Refresh token has expired. Please log in again.");

        var user = row.User;
        if (!user.IsActive)
        {
            await RevokeFamilyAsync(row.FamilyId, RefreshTokenRevocation.Deactivated);
            return RefreshOutcome.Fail(InvalidCode, "This account has been deactivated.");
        }

        // The new access token carries the same channel claims the login issued (seat enforcement).
        var claims = new List<Claim>();
        if (row.Channel == RefreshTokenChannels.StudentClient)
        {
            claims.Add(new Claim(SeatGuard.ClientClaim, SeatGuard.DesktopClient));
            if (!string.IsNullOrWhiteSpace(row.MachineName)) claims.Add(new Claim(SeatGuard.MachineClaim, row.MachineName));
        }

        var (accessToken, jti, expiry) = _jwt.GenerateToken(user, claims);
        _db.UserSessions.Add(new UserSession { UserId = user.UserId, Jti = jti, ExpiresAt = expiry });

        var (newRaw, successor) = Stage(user.UserId, jti, row.Channel, row.MachineName, ip, row.FamilyId, row.ExpiresAt);
        row.RevokedAt = now;
        row.RevokedReason = RefreshTokenRevocation.Rotated;
        row.ReplacedByTokenId = successor.TokenId;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request rotated (or revoked) this exact token between our read and write:
            // two parties hold it, so treat it exactly like a replay.
            _db.ChangeTracker.Clear();
            await HandleReuseAsync(row.UserId, row.FamilyId, row.Channel, ip);
            return RefreshOutcome.Fail(ReusedCode, "Refresh token was already used. Please log in again.");
        }

        var deviceBound = await _db.DeviceBindings.AnyAsync(d => d.UserId == user.UserId);
        return new RefreshOutcome(true, "SUCCESS", "Success", new TokenResponse(
            accessToken,
            newRaw,
            expiry,
            new UserSummary(user.UserId, Guid.Empty, user.Name, user.Email, user.Role.ToString()),
            deviceBound));
    }

    /// <summary>Logout: revokes the family that issued the access token <paramref name="jti"/>. Saves.</summary>
    public async Task<int> RevokeFamilyByJtiAsync(string jti, string reason)
    {
        var familyId = await _db.RefreshTokens.Where(t => t.Jti == jti).Select(t => (Guid?)t.FamilyId).FirstOrDefaultAsync();
        return familyId is Guid f ? await RevokeFamilyAsync(f, reason) : 0;
    }

    /// <summary>
    /// Force-logout / deactivation: revokes every live refresh token of the user and saves.
    /// Call before staging other changes — a concurrency retry clears the change tracker.
    /// </summary>
    public Task<int> RevokeAllForUserAsync(Guid userId, string reason) =>
        RevokeWhereAsync(t => t.UserId == userId, reason);

    private Task<int> RevokeFamilyAsync(Guid familyId, string reason) =>
        RevokeWhereAsync(t => t.FamilyId == familyId, reason);

    private async Task<int> RevokeWhereAsync(System.Linq.Expressions.Expression<Func<RefreshToken, bool>> filter, string reason)
    {
        for (var attempt = 1; ; attempt++)
        {
            var live = await _db.RefreshTokens.Where(filter).Where(t => t.RevokedAt == null).ToListAsync();
            if (live.Count == 0) return 0;

            var now = DateTime.UtcNow;
            foreach (var t in live)
            {
                t.RevokedAt = now;
                t.RevokedReason = reason;
            }

            try
            {
                await _db.SaveChangesAsync();
                return live.Count;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyRetries)
            {
                // A token was rotated concurrently; reload and revoke whatever is still live.
                _db.ChangeTracker.Clear();
            }
        }
    }

    private async Task HandleReuseAsync(Guid userId, Guid familyId, string channel, string? ip)
    {
        var revokedTokens = await RevokeFamilyAsync(familyId, RefreshTokenRevocation.Reuse);

        var jtis = await _db.RefreshTokens.Where(t => t.FamilyId == familyId).Select(t => t.Jti).ToListAsync();
        // Load-and-modify (ExecuteUpdate is not supported by the EF InMemory provider used in tests).
        var sessions = await _db.UserSessions.Where(s => jtis.Contains(s.Jti) && !s.IsRevoked).ToListAsync();
        sessions.ForEach(s => s.IsRevoked = true);

        _audit.Add(userId, "REFRESH_TOKEN_REUSE", "User", userId.ToString(),
            new { familyId, channel, ip, revokedTokens, revokedSessions = sessions.Count });
        await _db.SaveChangesAsync();
    }

    private (string raw, RefreshToken row) Stage(Guid userId, string jti, string channel, string? machineName, string? ip,
        Guid familyId, DateTime expiresAt)
    {
        var raw = NewRawToken();
        var row = new RefreshToken
        {
            UserId = userId,
            FamilyId = familyId,
            TokenHash = Hash(raw),
            Jti = jti,
            Channel = channel,
            MachineName = machineName,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            CreatedByIp = ip is null ? null : ip[..Math.Min(ip.Length, 64)]
        };
        _db.RefreshTokens.Add(row);
        return (raw, row);
    }
}
