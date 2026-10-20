using Backend_API.Services.Grading;

namespace Backend_API.Tests.Helpers;

/// <summary>
/// Module 11: which real code runners this machine has. Probed once per test run; tests of the real sandbox skip
/// (with the reason) instead of failing on a CI agent without Docker or MinGW.
/// </summary>
public static class RunnerAvailability
{
    public const string DockerImage = "gcc:13";

    private static readonly Lazy<bool> DockerLazy = new(() =>
        Environment.GetEnvironmentVariable("SMARTEXAM_SKIP_DOCKER_TESTS") != "1" &&
        DockerProbe.CheckAsync("docker", DockerImage).GetAwaiter().GetResult());

    private static readonly Lazy<string?> GppLazy = new(() => LocalMingwCodeRunner.FindGpp(null));

    public static bool Docker => DockerLazy.Value;
    public static string? Gpp => GppLazy.Value;
}

/// <summary>A fact that runs only when Docker answers and the gcc:13 image is pulled.</summary>
public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!RunnerAvailability.Docker)
            Skip = $"Docker with the {RunnerAvailability.DockerImage} image is not available (start Docker and run: docker pull {RunnerAvailability.DockerImage}).";
    }
}

/// <summary>A fact that runs only on Windows with a local g++ (MinGW) — the local runner's Job Object path.</summary>
public sealed class MingwFactAttribute : FactAttribute
{
    public MingwFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "The local runner's Job Object confinement is Windows-only.";
        else if (RunnerAvailability.Gpp is null)
            Skip = @"No g++ found (PATH or C:\MinGW\bin\g++.exe).";
    }
}
