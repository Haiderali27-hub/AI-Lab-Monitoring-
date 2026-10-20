using Backend_API.Models.Enums;

namespace Backend_API.Models;

public class MonitoringEvent
{
    public Guid EventId { get; set; } = Guid.NewGuid();
    public Guid ExamSessionId { get; set; }
    public MonitoringEventType EventType { get; set; }
    public string Payload { get; set; } = "{}";  // JSON string
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    // Module 5: id the desktop client gave the event when it queued it (offline replay). The server stores
    // a (session, ClientEventId) pair once, so a batch resent after a lost acknowledgement is not doubled.
    // Null for server-generated events and the single-event endpoint.
    public Guid? ClientEventId { get; set; }
    // When the server received it. RecordedAt keeps the client's time for a replayed event, so the
    // difference shows how long the client was offline.
    public DateTime? ReceivedAt { get; set; }

    // Navigation
    public ExamSession ExamSession { get; set; } = null!;
}
