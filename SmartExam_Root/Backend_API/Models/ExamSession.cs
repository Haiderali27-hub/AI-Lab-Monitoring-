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

    // Module 5: liveness. Every heartbeat stamps LastHeartbeatAt; HeartbeatWatchdogService flags a running
    // session whose client has been silent for Monitoring:DisconnectAfterSeconds (IsDisconnected + when),
    // and the next heartbeat clears it again (a Reconnected event records how long it was offline).
    public DateTime? LastHeartbeatAt { get; set; }
    public bool IsDisconnected { get; set; }
    public DateTime? DisconnectedAt { get; set; }

    // Module 10: a proctor suspended this student's lockdown until this instant (hub UnlockSession, e.g. while an
    // invigilator fixes the PC). The desktop client reads it from the heartbeat ack and resumes lockdown on its own
    // when it passes; null or in the past = locked as usual.
    public DateTime? UnlockedUntil { get; set; }

    // Navigation
    public Exam Exam { get; set; } = null!;
    public User Student { get; set; } = null!;
    public ICollection<Answer> Answers { get; set; } = new List<Answer>();
    public ICollection<MonitoringEvent> MonitoringEvents { get; set; } = new List<MonitoringEvent>();
}
