using System.Text.Json;
using Backend_API.Models.Enums;

namespace Backend_API.Models;

public class Exam
{
    public Guid ExamId { get; set; } = Guid.NewGuid();
    public Guid SectionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Instructions { get; set; }  // shown on the student exam dashboard
    public DateTime StartTime { get; set; }
    public int DurationMinutes { get; set; }
    public string AllowedApps { get; set; } = "[]";  // JSON array of exe names e.g. ["code.exe","codeblocks.exe"]
    public bool AiEvaluationEnabled { get; set; } = true;
    public int PlagiarismThreshold { get; set; } = 70;  // percentage
    public LockdownMode LockdownMode { get; set; } = LockdownMode.Report;
    public bool BlockMultiMonitor { get; set; } = true;
    // Module 2: start is refused until a passing readiness check (≤ 30 min old) exists.
    public bool RequireReadiness { get; set; }
    public bool RequireWebcam { get; set; }
    public bool RequireMic { get; set; }
    // Practice exam: unlimited attempts, excluded from results stats/analytics/plagiarism.
    public bool IsMock { get; set; }
    public ExamStatus Status { get; set; } = ExamStatus.Scheduled;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Section Section { get; set; } = null!;
    public ICollection<Question> Questions { get; set; } = new List<Question>();
    public ICollection<ExamAssignment> Assignments { get; set; } = new List<ExamAssignment>();
    public ICollection<ExamSession> Sessions { get; set; } = new List<ExamSession>();
}
