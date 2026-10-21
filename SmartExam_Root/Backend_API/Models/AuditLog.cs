namespace Backend_API.Models;

/// <summary>
/// One audit record. Module 12: the table is a tamper-evident hash chain — every row carries a gap-free
/// <see cref="Sequence"/>, the previous row's hash and its own <see cref="Hash"/> over its fields
/// (see <c>Services/AuditChain</c>). The three chain columns are null only for rows written before the
/// <c>AddAuditHashChain</c> migration, until <c>POST /api/audit-logs/backfill</c> seals them.
/// </summary>
public class AuditLog
{
    public Guid LogId { get; set; } = Guid.NewGuid();
    // The acting user (null for system events). A plain id, not a foreign key: the hash covers it, so it must
    // stay as written even after that user is deleted (a SET NULL cascade would break the chain).
    public Guid? ActorId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string Details { get; set; } = "{}";  // JSON
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Module 12 — hash chain (assigned by AppDbContext when the row is saved).
    public long? Sequence { get; set; }
    public string? PrevHash { get; set; }
    public string? Hash { get; set; }
}
