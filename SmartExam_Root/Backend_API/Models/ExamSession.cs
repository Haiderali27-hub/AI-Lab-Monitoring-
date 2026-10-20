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

    // Module 6 / 10: per-student extra time granted by a proctor (hub ExtendStudentTime). The student's
    // deadline is the exam end plus these minutes (SubmissionService.EffectiveEnd); ending the exam
    // (force-submit-all) still closes every session, extra time included.
    public int ExtraMinutes { get; set; }

    // Module 6: submission receipt, fixed when the session is finalised (SubmissionService / ReceiptHasher).
    // SHA-256 (lowercase hex) over the answers exactly as stored, so the student's copy can be checked later.
    public string? ReceiptSha256 { get; set; }
    public int? ReceiptAnswerCount { get; set; }

    // Navigation
    public Exam Exam { get; set; } = null!;
    public User Student { get; set; } = null!;
    public ICollection<Answer> Answers { get; set; } = new List<Answer>();
    public ICollection<MonitoringEvent> MonitoringEvents { get; set; } = new List<MonitoringEvent>();
}
