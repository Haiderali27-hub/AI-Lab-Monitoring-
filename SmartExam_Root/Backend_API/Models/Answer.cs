namespace Backend_API.Models;

public class Answer
{
    public Guid AnswerId { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public Guid QuestionId { get; set; }
    public string AnswerText { get; set; } = string.Empty;
    public DateTime? SubmittedAt { get; set; }
    public DateTime LastSavedAt { get; set; } = DateTime.UtcNow;

    // Module 6: client-side revision of this answer (incremented by the desktop app on every edit).
    // A save carrying a lower revision than the stored one is refused (409 STALE_REVISION), so an
    // offline queue replayed late can never overwrite a newer answer. 0 = only legacy (unrevisioned) saves.
    public int Revision { get; set; }

    // When the client says it saved the answer (diagnostics only: never trusted for the deadline).
    public DateTime? ClientSavedAt { get; set; }

    // Navigation
    public ExamSession ExamSession { get; set; } = null!;
    public Question Question { get; set; } = null!;
    public AiGradingResult? AiGradingResult { get; set; }
    public TeacherGradeOverride? TeacherGradeOverride { get; set; }
}
