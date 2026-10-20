namespace Backend_API.Models;

/// <summary>
/// Module 2 — one pre-exam readiness run reported by the student desktop client
/// (network, clock skew, screen capture, camera/mic, secure mode, disk).
/// </summary>
public class ReadinessCheck
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExamId { get; set; }
    public Guid UserId { get; set; }
    public bool PassedAll { get; set; }
    public string ResultsJson { get; set; } = "[]";  // [{name, passed, detail, durationMs}]
    public string? MachineName { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Exam Exam { get; set; } = null!;
    public User User { get; set; } = null!;
}
