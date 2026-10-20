namespace Backend_API.Models;

/// <summary>
/// Module 5 — one piece of capture evidence: a screen capture (all monitors) or a webcam snapshot uploaded by
/// the desktop client during an exam session. The image itself lives on the server's disk under
/// <c>Storage:EvidencePath</c> (<see cref="StoragePath"/> is relative to it and always server-generated); the row
/// carries the metadata and a SHA-256 of the stored bytes so the file can be checked later (evidence integrity).
/// Rows and files are deleted by <c>EvidenceRetentionService</c> after <c>Storage:RetentionDays</c>.
/// </summary>
public class Screenshot
{
    public const string KindScreen = "Screen";
    public const string KindWebcam = "Webcam";
    public const string TriggerInterval = "Interval";
    public const string TriggerViolation = "Violation";
    public const string TriggerManual = "Manual";

    public static readonly string[] Kinds = [KindScreen, KindWebcam];
    public static readonly string[] Triggers = [TriggerInterval, TriggerViolation, TriggerManual];

    public Guid ScreenshotId { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public Guid ExamId { get; set; }                 // denormalised: per-exam listing and storage folder
    public string Kind { get; set; } = KindScreen;   // Screen | Webcam
    public string Trigger { get; set; } = TriggerInterval; // Interval | Violation | Manual
    public string? TriggerReason { get; set; }       // e.g. "UnauthorizedProcess" for a violation capture
    public DateTime CapturedAt { get; set; }         // client capture time (clamped when absurd)
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string StoragePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/jpeg";
    public int SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;

    // Navigation
    public ExamSession Session { get; set; } = null!;
}
