using System.Text.Json;
using Backend_API.Data;
using Backend_API.Hubs;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend_API.Services.Grading;

/// <summary>
/// Module 11 — produces the <see cref="AiGradingResult"/> of every answer. Two halves:
/// <list type="bullet">
/// <item><see cref="StageAsync"/>, inside the submit request: MCQ and (offline) theory answers are graded on the spot; a
///       coding answer gets a Pending row with one Pending result per test case (so the result pages show the cases at
///       once) and its id is returned for the queue. Nothing slow runs here — submit stays well under a second.</item>
/// <item><see cref="ProcessAsync"/>, in the background <see cref="GradingWorker"/>: compiles and runs the code in the
///       sandbox, scores it, adds static analysis (and the optional LLM summary), writes the result and announces
///       <c>GradingCompleted</c> to the exam's live-monitor group.</item>
/// </list>
/// Suggestions only: a teacher override always wins, and the student never sees hidden test-case details.
/// </summary>
public sealed class GradingService(
    AppDbContext db,
    CodeRunnerResolver runners,
    LlmGrader llm,
    IHubContext<MonitoringHub> hub,
    IOptions<GradingOptions> options,
    AuditService audit,
    ILogger<GradingService> logger)
{
    public const string EngineMcq = "MCQ";
    // Module 12: every finished grading run (offline engine, sandbox or LLM) is part of the audit trail.
    public const string GradedAuditEvent = "AI_GRADING_COMPLETED";
    public const string FailedAuditEvent = "AI_GRADING_FAILED";
    public const string EngineNone = "None";
    public const int MaxEngineLength = 64;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly GradingOptions _options = options.Value;

    /// <summary>
    /// Stages grading rows for the answers of a session being finalised (caller saves). Returns the answers to queue
    /// AFTER the save — queued earlier, the worker could look for a row that is not committed yet.
    /// </summary>
    public async Task<List<Guid>> StageAsync(IReadOnlyList<Answer> answers)
    {
        var toQueue = new List<Guid>();
        if (answers.Count == 0) return toQueue;

        var answerIds = answers.Select(a => a.AnswerId).ToList();
        var alreadyGraded = (await db.AiGradingResults.Where(g => answerIds.Contains(g.AnswerId)).Select(g => g.AnswerId).ToListAsync()).ToHashSet();
        var questionIds = answers.Select(a => a.QuestionId).Distinct().ToList();
        var testCases = (await db.TestCases.AsNoTracking().Where(tc => questionIds.Contains(tc.QuestionId)).ToListAsync())
            .ToLookup(tc => tc.QuestionId);

        foreach (var answer in answers)
        {
            if (alreadyGraded.Contains(answer.AnswerId)) continue;
            var question = answer.Question;
            var row = new AiGradingResult { AnswerId = answer.AnswerId };

            switch (question.Type)
            {
                case QuestionType.Mcq:
                    GradeMcq(row, question, answer.AnswerText);
                    break;

                case QuestionType.Theory when llm.IsEnabled && !string.IsNullOrWhiteSpace(answer.AnswerText):
                    // The LLM call is slow and remote: queue it; the offline grade is the fallback.
                    MarkPending(row, "Queued for LLM-assisted grading — teacher review required until graded.");
                    toQueue.Add(answer.AnswerId);
                    break;

                case QuestionType.Theory:
                    Apply(row, TheoryGrader.Grade(question, answer.AnswerText));
                    break;

                default:    // Coding
                    if (string.IsNullOrWhiteSpace(answer.AnswerText))
                    {
                        Set(row, 0, "High", "No code was submitted.", EngineNone);
                    }
                    else if (!runners.CompilationEnabled)
                    {
                        SetNotRun(row, question, answer.AnswerText,
                            "Not compiled: code execution is turned off on this server (Grading:Runner = None) — teacher review required.");
                    }
                    else
                    {
                        var cases = OrderedCases(testCases[question.QuestionId]);
                        MarkPending(row, cases.Count == 0
                            ? "Queued for a sandboxed compile check (no test cases defined) — teacher review required until graded."
                            : $"Queued for sandboxed compile-and-run against {cases.Count} test case(s) — teacher review required until graded.");
                        AddPendingCases(row, cases);
                        toQueue.Add(answer.AnswerId);
                    }
                    break;
            }

            db.AiGradingResults.Add(row);
        }
        return toQueue;
    }

    /// <summary>
    /// Resets an answer's grading to Pending for another run (regrade). Caller saves, then enqueues the answer.
    /// False for MCQ answers, which are graded deterministically.
    /// </summary>
    public async Task<bool> RequeueAsync(Answer answer)
    {
        if (answer.Question.Type == QuestionType.Mcq) return false;

        var row = await db.AiGradingResults.Include(g => g.TestCaseResults).FirstOrDefaultAsync(g => g.AnswerId == answer.AnswerId);
        if (row is null)
        {
            row = new AiGradingResult { AnswerId = answer.AnswerId };
            db.AiGradingResults.Add(row);
        }
        else
        {
            db.TestCaseResults.RemoveRange(row.TestCaseResults);
            row.TestCaseResults.Clear();
        }

        row.QueueVersion++;
        row.CompileOutput = null;
        row.AnalysisJson = null;
        row.LlmSummary = null;
        row.DurationMs = null;
        if (answer.Question.Type == QuestionType.Coding)
        {
            var cases = OrderedCases(await db.TestCases.AsNoTracking().Where(tc => tc.QuestionId == answer.QuestionId).ToListAsync());
            MarkPending(row, "Re-queued for grading by staff — teacher review required until graded.");
            AddPendingCases(row, cases);
        }
        else
        {
            MarkPending(row, "Re-queued for grading by staff — teacher review required until graded.");
        }
        return true;
    }

    /// <summary>
    /// Grades one queued answer (worker). Does nothing if the row is gone or no longer Pending. An unexpected error
    /// (a bug, or the database refusing the result) marks the row Failed instead of leaving it Pending for ever — a
    /// Pending row would be re-run on every restart and keep the results page polling.
    /// </summary>
    public async Task ProcessAsync(Guid answerId, CancellationToken cancellationToken)
    {
        var row = await db.AiGradingResults
            .Include(g => g.TestCaseResults)
            .Include(g => g.Answer).ThenInclude(a => a.Question).ThenInclude(q => q.TestCases)
            .Include(g => g.Answer).ThenInclude(a => a.ExamSession)
            .FirstOrDefaultAsync(g => g.AnswerId == answerId, cancellationToken);
        if (row is null || row.Status != AiGradingResult.StatusPending) return;

        var answer = row.Answer;
        var question = answer.Question;
        var examId = answer.ExamSession.ExamId;
        var sessionId = answer.SessionId;
        var queueVersion = row.QueueVersion;
        string status;

        try
        {
            if (question.Type == QuestionType.Theory)
            {
                var graded = llm.IsEnabled && !string.IsNullOrWhiteSpace(answer.AnswerText)
                    ? await llm.GradeTheoryAsync(question, answer.AnswerText, cancellationToken)
                    : null;
                Apply(row, graded ?? TheoryGrader.Grade(question, answer.AnswerText));
            }
            else if (question.Type == QuestionType.Coding)
            {
                await GradeCodeAsync(row, question, answer.AnswerText, cancellationToken);
            }
            else
            {
                GradeMcq(row, question, answer.AnswerText);
            }

            status = row.Status;
            AuditRun(row, examId, sessionId, question.QuestionId);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A regrade (QueueVersion) or a deletion happened while this run was in flight: its result is stale.
            logger.LogInformation("Discarded a stale grading result for answer {AnswerId}.", answerId);
            return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;      // shutting down: the row stays Pending and is picked up on the next start
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Grading answer {AnswerId} failed unexpectedly; marking it Failed.", answerId);
            if (!await MarkFailedAsync(answerId, queueVersion, ex)) return;
            status = AiGradingResult.StatusFailed;
        }

        await hub.Clients.Group(ExamGroupTracker.GroupName(examId)).SendAsync("GradingCompleted", new
        {
            examId,
            sessionId,
            answerId,
            status
        }, CancellationToken.None);
    }

    /// <summary>
    /// Ends a run that broke outside the engine's own retries as Failed (0, Low, teacher review required), on a fresh
    /// read of the row and only if it is still the same Pending job — a regrade requested meanwhile is left alone.
    /// </summary>
    private async Task<bool> MarkFailedAsync(Guid answerId, int queueVersion, Exception error)
    {
        db.ChangeTracker.Clear();
        var row = await db.AiGradingResults.Include(g => g.TestCaseResults).FirstOrDefaultAsync(g => g.AnswerId == answerId);
        if (row is null || row.Status != AiGradingResult.StatusPending || row.QueueVersion != queueVersion) return false;

        Set(row, 0, "Low",
            $"Automatic grading failed (internal error: {error.GetType().Name}) — teacher review required.", row.Engine ?? EngineNone);
        row.Status = AiGradingResult.StatusFailed;
        foreach (var r in row.TestCaseResults) r.Status = TestCaseResult.StatusNotRun;
        audit.Add(null, FailedAuditEvent, "Answer", answerId.ToString(), new { error = error.GetType().Name, queueVersion });
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;   // regraded or deleted in the meantime
        }
    }

    private async Task GradeCodeAsync(AiGradingResult row, Question question, string source, CancellationToken cancellationToken)
    {
        var language = CodeLanguages.Resolve(question.Language, source);
        var runner = await runners.ResolveAsync(cancellationToken);
        if (runner is null)
        {
            SetNotRun(row, question, source,
                "Not compiled: no code runner is available on the server (Docker with the gcc image, or MinGW g++) — teacher review required.");
            return;
        }
        if (!runner.Supports(language))
        {
            SetNotRun(row, question, source,
                $"Not compiled: the answer looks like {language} code, and the sandbox compiles C and C++ only — teacher review required.");
            return;
        }

        var testCases = OrderedCases(question.TestCases);
        var compileOnly = testCases.Count == 0;
        List<ScoringCase> specs = compileOnly
            ? [new ScoringCase(null, TestCaseScorer.CompileOnlyCaseName, string.Empty, string.Empty, 1, false)]
            : testCases.Select((tc, i) => new ScoringCase(tc.TestCaseId, CaseName(i), tc.Input, tc.ExpectedOutput,
                tc.Weight, tc.IsHidden, tc.IsEdgeCase, tc.Category, new CompareOptions(tc.IgnoreCase, tc.FloatTolerance))).ToList();

        var job = new CodeJob(language, source,
            specs.Select((s, i) => new CodeJobCase(s.Input,
                compileOnly ? _options.DefaultTimeLimitMs : testCases[i].TimeLimitMs ?? _options.DefaultTimeLimitMs,
                s.ExpectedOutput)).ToList(),
            new CodeJobLimits(_options.CompileTimeoutSeconds, _options.MemoryLimitMb, _options.MaxOutputBytes));

        RunReport? report = null;
        Exception? lastError = null;
        var attempts = Math.Max(1, _options.MaxAttempts);
        for (var attempt = 1; attempt <= attempts && report is null; attempt++)
        {
            try
            {
                report = await runner.RunAsync(job, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                logger.LogWarning(ex, "Grading engine {Engine} failed (attempt {Attempt}/{Max}).", runner.Engine, attempt, attempts);
                if (attempt < attempts) await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
            }
        }

        if (report is null)
        {
            var reason = lastError?.Message ?? "unknown error";
            if (reason.Length > 300) reason = reason[..300] + "…";
            Set(row, 0, "Low", $"Automatic grading failed after {attempts} attempt(s) ({reason}) — teacher review required.", runner.Engine);
            row.Status = AiGradingResult.StatusFailed;
            foreach (var r in row.TestCaseResults) r.Status = TestCaseResult.StatusNotRun;
            row.AnalysisJson = JsonSerializer.Serialize(CodeAnalyzer.Analyze(source, language), Json);
            return;
        }

        var score = TestCaseScorer.Score(question.Marks, specs, report, compileOnly);
        var analysis = CodeAnalyzer.Analyze(source, language, compileOnly ? null : score);

        Set(row, score.SuggestedMarks, score.Confidence, score.Justification, report.Engine);
        row.CompileOutput = Truncate(report.CompileOutput, AiGradingResult.MaxCompileOutputLength);
        row.DurationMs = report.DurationMs;
        row.AnalysisJson = JsonSerializer.Serialize(analysis, Json);
        row.LlmSummary = llm.IsEnabled
            ? await llm.SummariseCodeAsync(question, score, analysis, report.Compiled, cancellationToken)
            : null;

        // Replace the pending rows with the outcome of every case.
        db.TestCaseResults.RemoveRange(row.TestCaseResults);
        row.TestCaseResults.Clear();
        foreach (var s in score.Cases)
        {
            db.TestCaseResults.Add(new TestCaseResult
            {
                ResultId = row.ResultId,
                TestCaseId = s.Case.TestCaseId,
                OrderIndex = s.Index,
                Name = s.Case.Name,
                Input = s.Case.Input,
                ExpectedOutput = s.Case.ExpectedOutput,
                IsHidden = s.Case.IsHidden,
                IsEdgeCase = s.Case.IsEdgeCase,
                Category = s.Case.Category,
                Weight = s.Case.Weight,
                Status = s.Status,
                Passed = s.Passed,
                ActualOutput = Truncate(s.ActualOutput, TestCaseResult.MaxOutputLength),
                ExitCode = s.ExitCode,
                DurationMs = s.DurationMs,
                CompareMode = s.CompareMode
            });
        }
    }

    /// <summary>
    /// True for a Graded row that carries no real suggestion: the code was not compiled (no runner, unsupported
    /// language), the theory question had no model answer or rubric, or only a compile check ran (no test cases). Its
    /// 0 means "teacher review required", so bulk accept leaves it for a manual mark. (A blank answer's 0 is a grade.)
    /// </summary>
    public static bool NeedsManualMark(AiGradingResult row) =>
        row.Status == AiGradingResult.StatusGraded &&
        row.Confidence == "Low" &&
        (row.Engine is EngineNone or TheoryGrader.EngineNone ||
         row.TestCaseResults.Any(r => r.TestCaseId == null && r.Name == TestCaseScorer.CompileOnlyCaseName));

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void GradeMcq(AiGradingResult row, Question question, string? answerText)
    {
        // Deterministic: the answer stores the selected option index.
        var correct = int.TryParse(answerText?.Trim(), out var selected) && selected == question.CorrectOptionIndex;
        Set(row, correct ? question.Marks : 0, "High",
            correct ? "Selected option matches the correct answer." : "Selected option does not match the correct answer.",
            EngineMcq);
    }

    private static void Apply(AiGradingResult row, TheoryGrade grade) =>
        Set(row, grade.SuggestedMarks, grade.Confidence, grade.Justification, grade.Engine);

    private void SetNotRun(AiGradingResult row, Question question, string source, string justification)
    {
        Set(row, 0, "Low", justification, EngineNone);
        db.TestCaseResults.RemoveRange(row.TestCaseResults);
        row.TestCaseResults.Clear();
        row.AnalysisJson = JsonSerializer.Serialize(CodeAnalyzer.Analyze(source, CodeLanguages.Resolve(question.Language, source)), Json);
    }

    private void AuditRun(AiGradingResult row, Guid examId, Guid sessionId, Guid questionId) =>
        audit.Add(null, row.Status == AiGradingResult.StatusFailed ? FailedAuditEvent : GradedAuditEvent, "Answer", row.AnswerId.ToString(), new
        {
            examId,
            sessionId,
            questionId,
            status = row.Status,
            engine = row.Engine,
            suggestedMarks = row.SuggestedMarks,
            confidence = row.Confidence
        });

    private static void Set(AiGradingResult row, double marks, string confidence, string justification, string engine)
    {
        row.SuggestedMarks = marks;
        row.Confidence = confidence;
        row.Justification = justification;
        // The column holds 64 characters; a long configured image or LLM model name must not make the save fail.
        row.Engine = engine.Length > MaxEngineLength ? engine[..(MaxEngineLength - 1)] + "…" : engine;
        row.Status = AiGradingResult.StatusGraded;
        row.GradedAt = DateTime.UtcNow;
    }

    private static void MarkPending(AiGradingResult row, string justification)
    {
        row.Status = AiGradingResult.StatusPending;
        row.SuggestedMarks = 0;
        row.Confidence = "Low";
        row.Justification = justification;
        row.Engine = null;
        row.GradedAt = DateTime.UtcNow;
    }

    // Added explicitly (not only through the navigation): new rows with preset keys reached through an already
    // tracked parent would otherwise be taken for existing rows and updated instead of inserted.
    private void AddPendingCases(AiGradingResult row, List<TestCase> cases)
    {
        void Add(TestCaseResult r)
        {
            row.TestCaseResults.Add(r);
            db.TestCaseResults.Add(r);
        }

        if (cases.Count == 0)
        {
            Add(new TestCaseResult { ResultId = row.ResultId, Name = TestCaseScorer.CompileOnlyCaseName });
            return;
        }
        for (var i = 0; i < cases.Count; i++)
        {
            var tc = cases[i];
            Add(new TestCaseResult
            {
                ResultId = row.ResultId,
                TestCaseId = tc.TestCaseId,
                OrderIndex = i,
                Name = CaseName(i),
                Input = tc.Input,
                ExpectedOutput = tc.ExpectedOutput,
                IsHidden = tc.IsHidden,
                IsEdgeCase = tc.IsEdgeCase,
                Category = tc.Category,
                Weight = tc.Weight
            });
        }
    }

    private static List<TestCase> OrderedCases(IEnumerable<TestCase> cases) =>
        cases.OrderBy(tc => tc.OrderIndex).ThenBy(tc => tc.TestCaseId).ToList();

    private static string CaseName(int index) => $"Case {index + 1}";

    private static string? Truncate(string? text, int max) =>
        text is null ? null : text.Length > max ? text[..max] + "…" : text;
}
