namespace Backend_API.Models;

public class MonitoringEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StudentUserId { get; set; }
    public Guid? ExamSessionId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public User StudentUser { get; set; } = null!;
    public ExamSession? ExamSession { get; set; }
}