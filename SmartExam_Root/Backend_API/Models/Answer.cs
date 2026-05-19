namespace Backend_API.Models;

public class Answer
{
    public Guid AnswerId { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public Guid QuestionId { get; set; }
    public string AnswerText { get; set; } = string.Empty;
    public DateTime? SubmittedAt { get; set; }
    public DateTime LastSavedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ExamSession ExamSession { get; set; } = null!;
    public Question Question { get; set; } = null!;
    public AiGradingResult? AiGradingResult { get; set; }
    public TeacherGradeOverride? TeacherGradeOverride { get; set; }
}
