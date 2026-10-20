using Backend_API.Helpers;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Data;

/// <summary>
/// Module 7 — no API can create the first SuperAdmin (an Admin may not create privileged accounts),
/// so an existing database without one is bootstrapped from configuration at startup:
///   Bootstrap:SuperAdminEmail     — required; an existing account with this email is promoted
///   Bootstrap:SuperAdminPassword  — only used when the account has to be created (min 8 chars)
/// Set them as environment variables (Bootstrap__SuperAdminEmail, …) for one start, never in a
/// committed file. Does nothing once any SuperAdmin exists.
/// </summary>
public static class SuperAdminBootstrap
{
    public const int MinPasswordLength = 8;

    public enum Outcome { AlreadyPresent, NotConfigured, Promoted, Created, InvalidPassword }

    public static async Task<Outcome> EnsureAsync(AppDbContext db, string? email, string? password)
    {
        if (await db.Users.AnyAsync(u => u.Role == UserRole.SuperAdmin)) return Outcome.AlreadyPresent;

        email = email?.Trim();
        if (string.IsNullOrEmpty(email)) return Outcome.NotConfigured;

        var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (existing is not null)
        {
            var previousRole = existing.Role;
            existing.Role = UserRole.SuperAdmin;
            existing.IsActive = true;
            AddAudit(db, existing.UserId, new { existing.Email, previousRole = previousRole.ToString(), mode = "promoted" });
            await db.SaveChangesAsync();
            return Outcome.Promoted;
        }

        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength) return Outcome.InvalidPassword;

        var salt = PasswordHelper.GenerateSalt();
        var user = new User
        {
            Name = "Super Admin",
            Email = email,
            Role = UserRole.SuperAdmin,
            Salt = salt,
            PasswordHash = PasswordHelper.HashPassword(password, salt)
        };
        db.Users.Add(user);
        AddAudit(db, user.UserId, new { user.Email, mode = "created" });
        await db.SaveChangesAsync();
        return Outcome.Created;
    }

    private static void AddAudit(AppDbContext db, Guid userId, object details) =>
        db.AuditLogs.Add(new AuditLog
        {
            ActorId = null, // the system, not a signed-in user
            EventType = "SUPERADMIN_BOOTSTRAPPED",
            EntityType = "User",
            EntityId = userId.ToString(),
            Details = System.Text.Json.JsonSerializer.Serialize(details),
            CreatedAt = DateTime.UtcNow
        });
}
