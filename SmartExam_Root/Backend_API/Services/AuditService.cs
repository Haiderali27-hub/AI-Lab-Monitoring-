using System.Text.Json;
using Backend_API.Data;
using Backend_API.Models;

namespace Backend_API.Services;

/// <summary>
/// Writes tamper-evidence records to the AuditLog table. Every security-relevant action
/// (logins, device-binding decisions, admin user changes) flows through here so the
/// system's "auditable" claim is real. Two modes:
///   • Add(...)   — stages a row on the current unit of work; caller's SaveChanges persists it.
///   • LogAsync() — stages + saves immediately (for standalone events like a failed login).
/// </summary>
public class AuditService
{
    private readonly AppDbContext _db;
    public AuditService(AppDbContext db) => _db = db;

    public void Add(Guid? actorId, string eventType, string entityType, string? entityId = null, object? details = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            ActorId = actorId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            Details = details is null ? "{}" : JsonSerializer.Serialize(details),
            CreatedAt = DateTime.UtcNow
        });
    }

    public async Task LogAsync(Guid? actorId, string eventType, string entityType, string? entityId = null, object? details = null)
    {
        Add(actorId, eventType, entityType, entityId, details);
        await _db.SaveChangesAsync();
    }
}
