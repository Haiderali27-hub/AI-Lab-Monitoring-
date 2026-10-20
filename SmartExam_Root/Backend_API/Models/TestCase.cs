namespace Backend_API.Models;

public class TestCase
{
    public const int MaxCategoryLength = 32;
    public const int MinTimeLimitMs = 100;
    public const int MaxTimeLimitMs = 10_000;
    public const double MaxWeight = 100;

    public Guid TestCaseId { get; set; } = Guid.NewGuid();
    public Guid QuestionId { get; set; }
    public string Input { get; set; } = string.Empty;
    public string ExpectedOutput { get; set; } = string.Empty;
    public bool IsHidden { get; set; } = false;

    // Module 11: scoring and comparison settings (see Services/Grading/TestCaseScorer and OutputComparer).
    // Marks = Σ weight of passed cases / Σ weights × question marks, so a weight-2 case counts double.
    public double Weight { get; set; } = 1;
    // Wall-clock limit for one run of this case; null = the runner default (Grading:DefaultTimeLimitMs, 2 s).
    public int? TimeLimitMs { get; set; }
    // Edge cases are reported separately and feed the "fails every edge case" hard-coding signal.
    public bool IsEdgeCase { get; set; }
    // Free label for per-category statistics ("basic", "large input", ...).
    public string? Category { get; set; }
    // Output comparison: letter case ignored, and numbers equal within this absolute tolerance (null = exact text).
    public bool IgnoreCase { get; set; }
    public double? FloatTolerance { get; set; }
    // Display order inside the question (the order the teacher entered the cases in).
    public int OrderIndex { get; set; }

    // Navigation
    public Question Question { get; set; } = null!;
}
