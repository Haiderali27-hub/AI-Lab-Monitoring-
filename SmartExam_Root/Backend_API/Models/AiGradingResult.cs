namespace Backend_API.Models;

public class AiGradingResult
{
    public Guid ResultId { get; set; } = Guid.NewGuid();
    public Guid AnswerId { get; set; }
    public double SuggestedMarks { get; set; }
    public string Justification { get; set; } = string.Empty;
    public string Confidence { get; set; } = "Medium";  // High / Medium / Low
    public DateTime GradedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Answer Answer { get; set; } = null!;
}
