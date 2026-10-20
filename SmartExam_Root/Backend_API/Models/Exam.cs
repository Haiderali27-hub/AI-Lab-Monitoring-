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
    // Module 3: start-session opens this many minutes before StartTime (0 = exactly at StartTime).
    public int EarlyJoinMinutes { get; set; }
    // Module 9: eligibility threshold used by "Recalculate from attendance".
    public double MinAttendancePercent { get; set; } = 75;
    // Module 9: when a seat is assigned, the student may only start/continue from that workstation.
    public bool EnforceSeat { get; set; } = true;
    // Module 8 (consumed by the Module 5 capture service): mean screenshot interval. The client
    // jitters each wait uniformly in [0.5x, 1.5x], so the default 60 s gives the thesis' random
    // 30–90 s schedule; a violation also triggers an immediate capture.
    public int ScreenshotIntervalSeconds { get; set; } = DefaultScreenshotIntervalSeconds;
    // Mean webcam snapshot interval (the on-device face check itself runs at 1–2 FPS on CPU).
    public int WebcamIntervalSeconds { get; set; } = DefaultWebcamIntervalSeconds;
    public ExamStatus Status { get; set; } = ExamStatus.Scheduled;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public const int DefaultScreenshotIntervalSeconds = 60;
    public const int DefaultWebcamIntervalSeconds = 60;
    public const int MinCaptureIntervalSeconds = 10;
    public const int MaxCaptureIntervalSeconds = 600;

    // Navigation
    public Section Section { get; set; } = null!;
    public ICollection<Question> Questions { get; set; } = new List<Question>();
    public ICollection<ExamAssignment> Assignments { get; set; } = new List<ExamAssignment>();
    public ICollection<ExamSession> Sessions { get; set; } = new List<ExamSession>();
}
