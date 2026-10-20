namespace Backend_API.Models;

/// <summary>
/// The automatic grading suggestion for one answer (Module 11). Never the final mark on its own: a
/// <see cref="TeacherGradeOverride"/> always wins, and Low / Medium confidence means "teacher review required".
/// MCQ and theory answers are graded synchronously on submit; coding answers are inserted as
/// <see cref="StatusPending"/> and graded by the background GradingWorker (sandboxed compile and run).
/// </summary>
public class AiGradingResult
{
    public const string StatusPending = "Pending";
    public const string StatusGraded = "Graded";
    public const string StatusFailed = "Failed";
    public const int MaxCompileOutputLength = 4096;

    public Guid ResultId { get; set; } = Guid.NewGuid();
    public Guid AnswerId { get; set; }
    public double SuggestedMarks { get; set; }
    public string Justification { get; set; } = string.Empty;
    public string Confidence { get; set; } = "Medium";  // High / Medium / Low
    public DateTime GradedAt { get; set; } = DateTime.UtcNow;

    // Pending (queued for the worker) / Graded / Failed (the engine broke after retries — teacher grades by hand).
    public string Status { get; set; } = StatusGraded;
    // What produced the suggestion: "MCQ", "TF-IDF", "Keyword heuristic", "Docker (gcc:13)", "Local MinGW (not isolated)", ...
    public string? Engine { get; set; }
    // Compiler diagnostics (truncated), shown to the teacher only.
    public string? CompileOutput { get; set; }
    // How long the sandbox run took (compile + every case), for the results page.
    public int? DurationMs { get; set; }
    // Static analysis of the code (loops, complexity signal, recursion, hard-coding flags) as JSON — teacher only.
    public string? AnalysisJson { get; set; }
    // Optional LLM-written summary for the teacher (only when Grading:Llm is enabled; off by default).
    public string? LlmSummary { get; set; }
    // Incremented whenever the answer is (re)queued. The worker writes its result only if the number is unchanged,
    // so a regrade requested while a run is in flight can never be overwritten by the older run.
    public int QueueVersion { get; set; }

    // Navigation
    public Answer Answer { get; set; } = null!;
    public ICollection<TestCaseResult> TestCaseResults { get; set; } = new List<TestCaseResult>();
}
