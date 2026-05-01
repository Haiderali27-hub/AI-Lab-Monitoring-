namespace Backend_API.Models;

public class ExamSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExamId { get; set; }
    public Guid StudentUserId { get; set; }
    public ExamSessionStatus Status { get; set; } = ExamSessionStatus.NotStarted;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }

    public Exam Exam { get; set; } = null!;
    public User StudentUser { get; set; } = null!;
    public ICollection<MonitoringEvent> MonitoringEvents { get; set; } = new List<MonitoringEvent>();
}