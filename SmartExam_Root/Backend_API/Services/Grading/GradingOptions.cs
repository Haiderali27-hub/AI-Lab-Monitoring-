namespace Backend_API.Services.Grading;

/// <summary>
/// Module 11 configuration (section <c>Grading</c>). Everything has a safe default, so an empty section means:
/// pick the Docker sandbox when Docker and the gcc image are available, otherwise the local MinGW runner
/// (labelled "not isolated"), otherwise no runner at all (coding answers are then left for the teacher).
/// The LLM features are off unless explicitly enabled AND an API key is present in the environment.
/// </summary>
public sealed class GradingOptions
{
    public const string Section = "Grading";

    /// <summary>Auto | Docker | Local | Fake | None. Fake is for tests (no code is compiled).</summary>
    public string Runner { get; set; } = RunnerKinds.Auto;

    /// <summary>Fake runner only: how long a "run" takes, so tests can observe the Pending state.</summary>
    public int FakeDelayMs { get; set; }

    /// <summary>How many answers are compiled and run at the same time.</summary>
    public int MaxParallel { get; set; } = 2;

    /// <summary>Wall-clock (and CPU) limit for one test case when the case sets none.</summary>
    public int DefaultTimeLimitMs { get; set; } = 2000;

    public int CompileTimeoutSeconds { get; set; } = 10;

    /// <summary>Memory limit for the student's program (and, in Docker, the whole container).</summary>
    public int MemoryLimitMb { get; set; } = 256;

    /// <summary>Program output kept per case; anything beyond is cut off (and the case usually fails).</summary>
    public int MaxOutputBytes { get; set; } = 64 * 1024;

    /// <summary>Attempts per answer when the engine itself fails (Docker down, disk full) before Status=Failed.</summary>
    public int MaxAttempts { get; set; } = 3;

    public DockerRunnerOptions Docker { get; set; } = new();
    public LocalRunnerOptions Local { get; set; } = new();
    public LlmOptions Llm { get; set; } = new();
}

public static class RunnerKinds
{
    public const string Auto = "Auto";
    public const string Docker = "Docker";
    public const string Local = "Local";
    public const string Fake = "Fake";
    public const string None = "None";
}

public sealed class DockerRunnerOptions
{
    /// <summary>The docker CLI (on PATH by default).</summary>
    public string Executable { get; set; } = "docker";
    /// <summary>Image with gcc/g++ (pre-pull it: <c>docker pull gcc:13</c>).</summary>
    public string Image { get; set; } = "gcc:13";
    public double Cpus { get; set; } = 0.5;
    public int PidsLimit { get; set; } = 64;
    /// <summary>How long a failed Docker probe is trusted before Auto asks Docker again.</summary>
    public int ProbeCacheSeconds { get; set; } = 60;
}

public sealed class LocalRunnerOptions
{
    /// <summary>Path to g++ (null: PATH, then C:\MinGW\bin\g++.exe). gcc is expected next to it for C answers.</summary>
    public string? GppPath { get; set; }
}

/// <summary>
/// Optional LLM grading of theory answers and a short summary for the teacher. OFF by default: it needs
/// <c>Grading:Llm:Enabled=true</c> AND the key in the environment variable named by <see cref="ApiKeyEnv"/>
/// (never in appsettings). Only the question, the model answer / rubric and the answer text are sent — no names,
/// emails or ids. Any failure falls back to the offline graders.
/// </summary>
public sealed class LlmOptions
{
    public bool Enabled { get; set; }
    /// <summary>Groq (OpenAI-compatible chat completions) or Gemini (generateContent).</summary>
    public string Provider { get; set; } = "Groq";
    public string? Model { get; set; }
    /// <summary>Environment variable holding the API key (default GROQ_API_KEY / GEMINI_API_KEY by provider).</summary>
    public string? ApiKeyEnv { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxAttempts { get; set; } = 3;
}
