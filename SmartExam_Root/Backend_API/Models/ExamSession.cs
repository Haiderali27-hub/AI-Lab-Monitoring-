using Backend_API.Models.Enums;

namespace Backend_API.Models;

public class ExamSession
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public Guid ExamId { get; set; }
    public Guid UserId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.InProgress;

    // Navigation
    public Exam Exam { get; set; } = null!;
    public User Student { get; set; } = null!;
    public ICollection<Answer> Answers { get; set; } = new List<Answer>();
    public ICollection<MonitoringEvent> MonitoringEvents { get; set; } = new List<MonitoringEvent>();
}
