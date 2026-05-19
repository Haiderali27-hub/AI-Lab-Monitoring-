using Backend_API.Models.Enums;

namespace Backend_API.Models;

public class MonitoringEvent
{
    public Guid EventId { get; set; } = Guid.NewGuid();
    public Guid ExamSessionId { get; set; }
    public MonitoringEventType EventType { get; set; }
    public string Payload { get; set; } = "{}";  // JSON string
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ExamSession ExamSession { get; set; } = null!;
}
