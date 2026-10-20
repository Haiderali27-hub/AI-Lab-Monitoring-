using Microsoft.Extensions.Options;

namespace Backend_API.Services.Grading;

/// <summary>
/// Picks the code runner for a grading job from <c>Grading:Runner</c>:
/// <list type="bullet">
/// <item><b>Auto</b> (default): Docker when the daemon answers and the gcc image is pulled; else the local MinGW runner
///       when g++ exists; else none.</item>
/// <item><b>Docker</b> / <b>Local</b>: that runner only (if it cannot run, the answer ends up Failed after retries, so a
///       misconfiguration is visible rather than silently downgraded).</item>
/// <item><b>Fake</b>: the test runner. <b>None</b>: no compilation; coding answers are left for the teacher.</item>
/// </list>
/// Resolution happens in the background worker, never on the submit request.
/// </summary>
public sealed class CodeRunnerResolver(
    IOptions<GradingOptions> options,
    DockerProbe dockerProbe,
    DockerCodeRunner docker,
    LocalMingwCodeRunner local)
{
    private readonly GradingOptions _options = options.Value;

    /// <summary>False only when the configuration turns compilation off (Runner = None).</summary>
    public bool CompilationEnabled => !string.Equals(_options.Runner, RunnerKinds.None, StringComparison.OrdinalIgnoreCase);

    public async Task<ICodeRunner?> ResolveAsync(CancellationToken cancellationToken)
    {
        switch (_options.Runner.Trim().ToLowerInvariant())
        {
            case "fake":
                return new FakeCodeRunner(_options.FakeDelayMs);
            case "docker":
                return docker;
            case "local":
                return local;
            case "none":
                return null;
            default:
                if (await dockerProbe.IsAvailableAsync(cancellationToken)) return docker;
                return local.IsAvailable ? local : null;
        }
    }
}
