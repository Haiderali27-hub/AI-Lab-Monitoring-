namespace Backend_API.Models;

/// <summary>
/// Module 11 — the outcome of one test case for one graded coding answer. Rows belong to the
/// <see cref="AiGradingResult"/> and are replaced on every regrade. Input and expected output are copied from the
/// test case when the answer is queued, so the result stays readable even for the synthetic "Compile &amp; run"
/// case of a question without test cases. Hidden cases are shown to staff only (the student result
/// endpoint drops them and reports just how many passed).
/// </summary>
public class TestCaseResult
{
    public const string StatusPending = "Pending";
    public const string StatusPassed = "Passed";
    public const string StatusFailed = "Failed";
    public const string StatusTimedOut = "TimedOut";
    public const string StatusMemoryExceeded = "MemoryExceeded";
    public const string StatusRuntimeError = "RuntimeError";
    public const string StatusNotRun = "NotRun";            // compile error: the case never ran
    public const int MaxOutputLength = 4096;

    public Guid TestCaseResultId { get; set; } = Guid.NewGuid();
    public Guid ResultId { get; set; }
    public Guid? TestCaseId { get; set; }                   // null for the synthetic compile-only case
    public int OrderIndex { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Input { get; set; } = string.Empty;
    public string ExpectedOutput { get; set; } = string.Empty;
    public bool IsHidden { get; set; }
    public bool IsEdgeCase { get; set; }
    public string? Category { get; set; }
    public double Weight { get; set; } = 1;

    public string Status { get; set; } = StatusPending;
    public bool Passed { get; set; }
    public string? ActualOutput { get; set; }
    public int? ExitCode { get; set; }
    public int? DurationMs { get; set; }
    // How the output matched when it passed: exact / trimmed lines / ignore case / numeric tolerance.
    public string? CompareMode { get; set; }

    // Navigation
    public AiGradingResult AiGradingResult { get; set; } = null!;
}
