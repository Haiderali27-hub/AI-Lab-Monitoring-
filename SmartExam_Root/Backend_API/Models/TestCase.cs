namespace Backend_API.Models;

public class TestCase
{
    public Guid TestCaseId { get; set; } = Guid.NewGuid();
    public Guid QuestionId { get; set; }
    public string Input { get; set; } = string.Empty;
    public string ExpectedOutput { get; set; } = string.Empty;
    public bool IsHidden { get; set; } = false;

    // Navigation
    public Question Question { get; set; } = null!;
}
