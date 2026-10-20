using System.Text;
using Backend_API.Models;

namespace Backend_API.Services.Grading;

/// <summary>A test case as the scorer sees it (copied from <see cref="TestCase"/>, or the synthetic compile-only case).</summary>
public sealed record ScoringCase(
    Guid? TestCaseId,
    string Name,
    string Input,
    string ExpectedOutput,
    double Weight,
    bool IsHidden,
    bool IsEdgeCase = false,
    string? Category = null,
    CompareOptions? Compare = null);

public sealed record ScoredCase(
    int Index,
    ScoringCase Case,
    string Status,
    bool Passed,
    string? ActualOutput,
    int? ExitCode,
    int? DurationMs,
    string? CompareMode);

public sealed record CategoryStat(string Category, int Passed, int Total);

public sealed record CodeScore(
    double SuggestedMarks,
    string Confidence,
    string Justification,
    IReadOnlyList<ScoredCase> Cases,
    double PassRate,
    bool NearCorrect,
    IReadOnlyList<CategoryStat> Categories);

/// <summary>
/// Module 11 — turns a runner's <see cref="RunReport"/> into a suggested mark. Pure (unit tests M11-U-03…06).
/// <list type="bullet">
/// <item>Marks = Σ weight of passed cases / Σ weights × question marks, rounded to the nearest 0.5.</item>
/// <item>A compile error gives 0 with High confidence and the compiler output in the justification.</item>
/// <item>A timeout, crash or memory kill fails only that case.</item>
/// <item>Confidence: High when every case passed or every case failed, Medium for a partial pass, Low for a
///       compile-only check (question without test cases).</item>
/// <item>The justification never quotes a hidden case's input, expected or actual output ("Hidden case #2: failed"),
///       because the student sees it too. Staff get the full detail from the per-case results.</item>
/// </list>
/// </summary>
public static class TestCaseScorer
{
    public const string CompileOnlyCaseName = "Compile & run";
    private const int QuoteLength = 60;
    private const int CompilerExcerptLength = 1200;

    public static double RoundToHalf(double marks) => Math.Round(marks * 2, MidpointRounding.AwayFromZero) / 2;

    public static CodeScore Score(int questionMarks, IReadOnlyList<ScoringCase> cases, RunReport report, bool compileOnly = false)
    {
        if (!report.Compiled)
        {
            var notRun = cases.Select((c, i) => new ScoredCase(i, c, TestCaseResult.StatusNotRun, false, null, null, null, null)).ToList();
            var compiler = Excerpt(report.CompileOutput, CompilerExcerptLength);
            var text = $"Compilation failed, so no test case could run: 0 / {questionMarks}. Compiler output:\n{compiler}";
            return new CodeScore(0, "High", text, notRun, 0, false, Categories(notRun));
        }

        var scored = cases.Select((c, i) => ScoreCase(i, c, report.Cases.FirstOrDefault(r => r.Index == i))).ToList();

        if (compileOnly)
        {
            var run = scored.FirstOrDefault();
            var outcome = run is { Passed: true }
                ? "It compiled and ran without errors"
                : $"It compiled but did not run cleanly ({Describe(run)})";
            var text = $"No test cases are defined for this question — compile check only. {outcome}; correctness was not " +
                       "checked, so no marks are suggested — teacher review required.";
            return new CodeScore(0, "Low", text, scored, run is { Passed: true } ? 1 : 0, false, Categories(scored));
        }

        // Weights are validated (> 0) when the exam is saved; equal weights if they somehow sum to nothing.
        var totalWeight = scored.Sum(s => Math.Max(0, s.Case.Weight));
        double WeightOf(ScoredCase s) => totalWeight > 0 ? Math.Max(0, s.Case.Weight) : 1;
        var denominator = totalWeight > 0 ? totalWeight : scored.Count;
        var passedWeight = scored.Where(s => s.Passed).Sum(WeightOf);
        var passRate = denominator > 0 ? passedWeight / denominator : 0;
        var marks = RoundToHalf(passRate * questionMarks);

        var passed = scored.Count(s => s.Passed);
        var failed = scored.Count - passed;
        var nearCorrect = failed > 0 && failed <= 2 && passRate >= 0.8;
        var confidence = passed == scored.Count || passed == 0 ? "High" : "Medium";

        var sb = new StringBuilder();
        sb.Append($"Compiled and ran against {scored.Count} test case(s): {passed} passed");
        if (scored.Select(WeightOf).Distinct().Count() > 1) sb.Append($" (weighted {passRate:P0})");
        sb.Append($" → {Format(marks)} / {questionMarks}.");
        foreach (var s in scored) sb.Append('\n').Append(Line(s));
        if (nearCorrect)
            sb.Append("\nNear-correct: at least 80% passed with at most two failures — consider partial credit.");
        if (confidence == "Medium")
            sb.Append("\nPartial pass — teacher review recommended.");

        return new CodeScore(marks, confidence, sb.ToString(), scored, passRate, nearCorrect, Categories(scored));
    }

    private static ScoredCase ScoreCase(int index, ScoringCase c, CaseRun? run)
    {
        if (run is null) return new ScoredCase(index, c, TestCaseResult.StatusNotRun, false, null, null, null, null);

        if (run.TimedOut)
            return new ScoredCase(index, c, TestCaseResult.StatusTimedOut, false, run.Stdout, run.ExitCode, run.DurationMs, null);
        if (run.MemoryExceeded)
            return new ScoredCase(index, c, TestCaseResult.StatusMemoryExceeded, false, run.Stdout, run.ExitCode, run.DurationMs, null);
        if (run.ExitCode is not 0)
            return new ScoredCase(index, c, TestCaseResult.StatusRuntimeError, false, run.Stdout, run.ExitCode, run.DurationMs, null);

        var (ok, mode) = OutputComparer.Compare(c.ExpectedOutput, run.Stdout, c.Compare);
        return new ScoredCase(index, c, ok ? TestCaseResult.StatusPassed : TestCaseResult.StatusFailed, ok,
            run.Stdout, run.ExitCode, run.DurationMs, mode);
    }

    private static string Line(ScoredCase s)
    {
        var label = s.Case.IsHidden ? $"Hidden case #{s.Index + 1}" : $"Case {s.Index + 1}";
        if (s.Passed) return $"{label}: passed.";
        if (s.Case.IsHidden || s.Status != TestCaseResult.StatusFailed) return $"{label}: failed ({Describe(s)}).";
        return $"{label}: failed — expected {Quote(s.Case.ExpectedOutput)}, got {Quote(s.ActualOutput)}.";
    }

    private static string Describe(ScoredCase? s) => s?.Status switch
    {
        TestCaseResult.StatusTimedOut => "time limit exceeded",
        TestCaseResult.StatusMemoryExceeded => "memory limit exceeded",
        TestCaseResult.StatusRuntimeError => $"runtime error, exit code {s.ExitCode}",
        TestCaseResult.StatusNotRun => "not run",
        _ => "wrong output"
    };

    private static List<CategoryStat> Categories(IEnumerable<ScoredCase> cases) =>
        cases.GroupBy(s => s.Case.IsEdgeCase ? "edge" : string.IsNullOrWhiteSpace(s.Case.Category) ? "general" : s.Case.Category!.Trim())
            .Select(g => new CategoryStat(g.Key, g.Count(s => s.Passed), g.Count()))
            .ToList();

    private static string Quote(string? text)
    {
        var t = OutputComparer.Normalise(text).Replace("\n", "⏎");
        return "\"" + (t.Length > QuoteLength ? t[..QuoteLength] + "…" : t) + "\"";
    }

    private static string Excerpt(string? text, int max)
    {
        var t = (text ?? "(no compiler output)").Trim();
        return t.Length > max ? t[..max] + "\n…" : t;
    }

    private static string Format(double marks) => marks.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}
