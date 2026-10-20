namespace Backend_API.Models;

/// <summary>
/// Module 1: one rotating refresh token. Only the SHA-256 hash of the raw token is stored.
/// Every login starts a new <see cref="FamilyId"/>; each successful refresh revokes the presented
/// token ("rotated") and issues its successor in the same family. Presenting an already-rotated
/// token again is treated as theft: the whole family and every access token it issued are revoked.
/// </summary>
public class RefreshToken
{
    public Guid TokenId { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }
    public string TokenHash { get; set; } = string.Empty;   // SHA-256 of the raw token (unique)
    public string Jti { get; set; } = string.Empty;         // the access token issued together with it
    public string Channel { get; set; } = RefreshTokenChannels.Web;
    public string? MachineName { get; set; }                // re-emitted as the "machine" claim on refresh (seat enforcement)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }                 // absolute: rotation never extends the family's lifetime
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }              // see RefreshTokenRevocation
    public Guid? ReplacedByTokenId { get; set; }
    public string? CreatedByIp { get; set; }

    // Navigation
    public User User { get; set; } = null!;
}

public static class RefreshTokenChannels
{
    public const string Web = "web";
    public const string StudentClient = "student-client";
}

public static class RefreshTokenRevocation
{
    public const string Rotated = "rotated";
    public const string Reuse = "reuse";
    public const string Logout = "logout";
    public const string ForceLogout = "force_logout";
    public const string Deactivated = "deactivated";
}
