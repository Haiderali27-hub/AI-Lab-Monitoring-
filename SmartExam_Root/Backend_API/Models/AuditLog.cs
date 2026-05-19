namespace Backend_API.Models;

public class AuditLog
{
    public Guid LogId { get; set; } = Guid.NewGuid();
    public Guid? ActorId { get; set; }  // null for system events
    public string EventType { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string Details { get; set; } = "{}";  // JSON
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User? Actor { get; set; }
}
