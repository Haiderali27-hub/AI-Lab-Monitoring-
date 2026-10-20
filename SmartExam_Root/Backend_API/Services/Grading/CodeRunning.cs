using System.Text.RegularExpressions;

namespace Backend_API.Services.Grading;

/// <summary>One program to compile once and run against every case (cases in order; stdin = Input).</summary>
public sealed record CodeJob(string Language, string Source, IReadOnlyList<CodeJobCase> Cases, CodeJobLimits Limits);

/// <summary>
/// One case. <see cref="ExpectedOutput"/> is never handed to the student's program (real runners do not even write
/// it into the sandbox); only the fake test runner reads it, to "pass" a case.
/// </summary>
public sealed record CodeJobCase(string Input, int TimeLimitMs, string? ExpectedOutput = null);

public sealed record CodeJobLimits(int CompileTimeoutSeconds, int MemoryLimitMb, int MaxOutputBytes);

/// <summary>What happened to one case. <see cref="Stdout"/> is capped at the job's MaxOutputBytes.</summary>
public sealed record CaseRun(
    int Index,
    string Stdout,
    int? ExitCode,
    bool TimedOut,
    bool MemoryExceeded,
    int DurationMs,
    bool OutputTruncated = false);

/// <summary>The runner's raw outcome. Grading (comparison, weights, marks) is <see cref="TestCaseScorer"/>'s job.</summary>
public sealed record RunReport(bool Compiled, string? CompileOutput, IReadOnlyList<CaseRun> Cases, string Engine, int DurationMs);

/// <summary>
/// Compiles and runs untrusted student code. Implementations must enforce the limits in <see cref="CodeJob.Limits"/>
/// and per case, never let one case's timeout stop the others, and throw <see cref="GradingInfrastructureException"/>
/// only when the engine itself failed (so the worker retries) — a student's compile error or crash is a normal
/// <see cref="RunReport"/>.
/// </summary>
public interface ICodeRunner
{
    /// <summary>Shown on the results page, e.g. "Docker (gcc:13, isolated)" or "Local MinGW g++ (not isolated)".</summary>
    string Engine { get; }

    /// <summary>True when the program runs without network and outside the server's file system.</summary>
    bool Isolated { get; }

    bool Supports(string language);

    Task<RunReport> RunAsync(CodeJob job, CancellationToken cancellationToken);
}

/// <summary>The grading engine broke (Docker daemon down, compiler missing, disk full): retry, then mark Failed.</summary>
public sealed class GradingInfrastructureException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Languages the code runners understand, and detection when the question does not say.</summary>
public static class CodeLanguages
{
    public const string Cpp = "cpp";
    public const string C = "c";
    public const string Python = "python";
    public const string Unknown = "unknown";

    public static readonly string[] Supported = [Cpp, C];

    private static readonly Regex CppMarkers = new(
        @"#\s*include\s*<(iostream|bits/stdc\+\+\.h|vector|string|algorithm|map|set|queue|stack|cmath|cstdio|cstdlib|cstring)>|\bstd::|\bcout\b|\bcin\b|\busing\s+namespace\b|\bclass\s+\w+|\btemplate\s*<|\bnew\s+\w+|\bdelete\s*\[?",
        RegexOptions.Compiled);
    private static readonly Regex CMarkers = new(@"#\s*include\s*<\w+(/\w+)*\.h>|\bprintf\s*\(|\bscanf\s*\(|\bmalloc\s*\(", RegexOptions.Compiled);
    private static readonly Regex PythonMarkers = new(@"^\s*(def|import|from)\s+\w+|^\s*print\s*\(|\binput\s*\(\s*\)", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// The question's language when set (normalised: "c++" → cpp), otherwise a guess from the code: C++ markers win,
    /// then C (C code such as <c>char* p = malloc(n)</c> is an error in C++, so it must not be compiled as C++),
    /// then Python. Anything else is compiled as C++.
    /// </summary>
    public static string Resolve(string? questionLanguage, string source)
    {
        var configured = Normalise(questionLanguage);
        if (configured is not null) return configured;

        if (CppMarkers.IsMatch(source)) return Cpp;
        if (CMarkers.IsMatch(source)) return C;
        if (!source.Contains('{') && PythonMarkers.IsMatch(source)) return Python;
        return Cpp;
    }

    /// <summary>"cpp", "c", "python" or null for empty / unknown values.</summary>
    public static string? Normalise(string? language) => language?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "cpp" or "c++" or "cxx" => Cpp,
        "c" => C,
        "python" or "py" or "python3" => Python,
        _ => null
    };
}

/// <summary>
/// Test runner: no compiler. Waits <c>Grading:FakeDelayMs</c>, then echoes each case's expected output — every case
/// passes unless the source contains a marker: <c>#compile-error</c> (compilation fails), <c>#fail</c> (wrong output),
/// <c>#timeout</c> (every case times out) or <c>#memory</c> (every case exceeds the memory limit).
/// </summary>
public sealed class FakeCodeRunner(int delayMs) : ICodeRunner
{
    public const string EngineName = "Fake runner (tests)";

    public string Engine => EngineName;
    public bool Isolated => true;
    public bool Supports(string language) => language is CodeLanguages.Cpp or CodeLanguages.C;

    public async Task<RunReport> RunAsync(CodeJob job, CancellationToken cancellationToken)
    {
        if (delayMs > 0) await Task.Delay(delayMs, cancellationToken);

        if (job.Source.Contains("#compile-error"))
            return new RunReport(false, "main.cpp:1:1: error: fake compile error requested by the test", [], Engine, 1);

        var timeout = job.Source.Contains("#timeout");
        var memory = job.Source.Contains("#memory");
        var fail = job.Source.Contains("#fail");
        var cases = job.Cases.Select((c, i) => new CaseRun(
            i,
            timeout || memory ? string.Empty : fail ? "wrong output" : c.ExpectedOutput ?? string.Empty,
            timeout ? null : memory ? 137 : 0,
            timeout,
            memory,
            timeout ? c.TimeLimitMs : 1)).ToList();
        return new RunReport(true, null, cases, Engine, Math.Max(1, delayMs));
    }
}
