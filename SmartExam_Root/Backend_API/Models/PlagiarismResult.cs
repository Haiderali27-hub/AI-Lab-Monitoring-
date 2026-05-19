namespace Backend_API.Models;

public class PlagiarismResult
{
    public Guid PlagId { get; set; } = Guid.NewGuid();
    public Guid ExamId { get; set; }
    public Guid QuestionId { get; set; }
    public Guid UserIdA { get; set; }
    public Guid UserIdB { get; set; }
    public double SimilarityScore { get; set; }
    public string MatchingSegments { get; set; } = "[]";  // JSON
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Exam Exam { get; set; } = null!;
    public Question Question { get; set; } = null!;
    public User StudentA { get; set; } = null!;
    public User StudentB { get; set; } = null!;
}
