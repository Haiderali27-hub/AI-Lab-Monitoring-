using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;

namespace Backend_API.Services.Grading;

/// <summary>
/// Module 11 — the real sandbox: one throw-away container per answer from the gcc image, with
/// <c>--network none</c>, a memory cap (swap included), half a CPU, a PID limit (no fork bombs), a read-only root,
/// all capabilities dropped, the unprivileged <c>nobody</c> user, and the answer mounted read-only. A generated
/// shell harness compiles once (<c>-std=c++17 -O0</c>, the same flags as the local runner, so an answer gets the same
/// grade on both; unoptimised so the program does what the student wrote — an -O2 build deletes an unused 1 GB
/// allocation or an empty endless loop and would hide memory and time-limit breaches) and runs every case with its own time limit, reporting exit code, duration, an OOM-kill flag
/// (from the container's cgroup <c>memory.events</c>) and the base64 output per case. Expected outputs never enter the
/// container. The host kills the container if the whole run overruns.
/// </summary>
public sealed class DockerCodeRunner(IOptions<GradingOptions> options, ILogger<DockerCodeRunner> logger) : ICodeRunner
{
    private readonly GradingOptions _options = options.Value;

    public string Engine => $"Docker ({_options.Docker.Image}, isolated)";
    public bool Isolated => true;
    public bool Supports(string language) => language is CodeLanguages.Cpp or CodeLanguages.C;

    public async Task<RunReport> RunAsync(CodeJob job, CancellationToken cancellationToken)
    {
        if (!Supports(job.Language))
            throw new GradingInfrastructureException($"The Docker runner does not support '{job.Language}'.");

        var work = Path.Combine(Path.GetTempPath(), "smartexam-grading", Guid.NewGuid().ToString("N"));
        var container = "smartexam-grade-" + Guid.NewGuid().ToString("N")[..16];
        var started = DateTime.UtcNow;
        try
        {
            Directory.CreateDirectory(work);
            var sourceFile = job.Language == CodeLanguages.C ? "main.c" : "main.cpp";
            await File.WriteAllTextAsync(Path.Combine(work, sourceFile), job.Source, new UTF8Encoding(false), cancellationToken);
            for (var i = 0; i < job.Cases.Count; i++)
                await File.WriteAllTextAsync(Path.Combine(work, $"in_{i}.txt"), GradingProcess.NormaliseInput(job.Cases[i].Input),
                    new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(work, "harness.sh"), BuildHarness(job, sourceFile), new UTF8Encoding(false), cancellationToken);
            MakeReadableByNobody(work);

            var limits = job.Limits;
            var args = new List<string>
            {
                "run", "--rm", "--name", container,
                "--network", "none",
                "--memory", $"{limits.MemoryLimitMb}m", "--memory-swap", $"{limits.MemoryLimitMb}m",
                "--cpus", _options.Docker.Cpus.ToString(CultureInfo.InvariantCulture),
                "--pids-limit", _options.Docker.PidsLimit.ToString(CultureInfo.InvariantCulture),
                "--read-only", "--tmpfs", "/tmp:rw,exec,nosuid,size=64m",
                "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
                "--user", "65534:65534",
                "-v", $"{work}:/src:ro", "-w", "/tmp",
                _options.Docker.Image, "sh", "/src/harness.sh"
            };

            // Container start + compile + every case (+1 s kill grace each) + slack for a busy machine.
            var budget = TimeSpan.FromSeconds(limits.CompileTimeoutSeconds + 30)
                         + TimeSpan.FromMilliseconds(job.Cases.Sum(c => c.TimeLimitMs + 1500));
            var result = await GradingProcess.RunAsync(_options.Docker.Executable, args, budget, cancellationToken,
                onTimeout: () => KillAsync(container));

            if (result.TimedOut)
                throw new GradingInfrastructureException($"The sandbox did not finish within {budget.TotalSeconds:0} s.");

            var report = Parse(result.Stdout, job, Engine, (int)(DateTime.UtcNow - started).TotalMilliseconds);
            if (report is null)
            {
                var detail = result.Stderr.Length > 500 ? result.Stderr[..500] : result.Stderr;
                throw new GradingInfrastructureException($"docker exited with code {result.ExitCode}: {detail.Trim()}");
            }
            return report;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await KillAsync(container);
            throw;
        }
        finally
        {
            await GradingProcess.DeleteDirectoryAsync(work);
        }
    }

    private async Task KillAsync(string container)
    {
        try
        {
            await GradingProcess.RunAsync(_options.Docker.Executable, ["kill", container], TimeSpan.FromSeconds(15), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not kill grading container {Container}.", container);
        }
    }

    // On Linux hosts the folder is private to the API's user; the container runs as nobody.
    private static void MakeReadableByNobody(string work)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(work, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                   UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        foreach (var file in Directory.GetFiles(work))
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
    }

    /// <summary>The POSIX sh script run inside the container (LF line endings; dash-compatible).</summary>
    public static string BuildHarness(CodeJob job, string sourceFile)
    {
        var compiler = job.Language == CodeLanguages.C
            ? $"gcc -std=c11 -O0 -pipe -o /tmp/main /src/{sourceFile} -lm"
            : $"g++ -std=c++17 -O0 -pipe -o /tmp/main /src/{sourceFile}";
        var compileSeconds = job.Limits.CompileTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        var maxOutput = job.Limits.MaxOutputBytes.ToString(CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append("#!/bin/sh\n");
        sb.Append("# Generated by SmartExam (Module 11): compile once, run each case with its own limit.\n");
        sb.Append("oom_kills() { awk '$1 == \"oom_kill\" { print $2 }' /sys/fs/cgroup/memory.events 2>/dev/null; }\n");
        sb.Append($"timeout -k 1 {compileSeconds} {compiler} > /tmp/compile.txt 2>&1\n");
        sb.Append("rc=$?\n");
        sb.Append("if [ $rc -ne 0 ]; then\n");
        sb.Append($"  [ $rc -eq 124 ] && echo \"Compilation timed out after {compileSeconds} s.\" >> /tmp/compile.txt\n");
        sb.Append("  echo \"COMPILE_FAILED $(head -c 4096 /tmp/compile.txt | base64 -w0)\"\n");
        sb.Append("  exit 0\n");
        sb.Append("fi\n");
        sb.Append("echo \"COMPILED $(head -c 4096 /tmp/compile.txt | base64 -w0)\"\n");
        sb.Append("run_case() {\n");
        sb.Append("  before=$(oom_kills)\n");
        sb.Append("  start=$(date +%s%N)\n");
        sb.Append("  timeout -k 1 \"$2\" /tmp/main < \"/src/in_$1.txt\" > /tmp/out.txt 2>/dev/null\n");
        sb.Append("  rc=$?\n");
        sb.Append("  end=$(date +%s%N)\n");
        sb.Append("  after=$(oom_kills)\n");
        sb.Append("  mem=0\n");
        sb.Append("  [ \"$before\" != \"$after\" ] && mem=1\n");
        sb.Append($"  echo \"CASE $1 $rc $(( (end - start) / 1000000 )) $mem $(head -c {maxOutput} /tmp/out.txt | base64 -w0)\"\n");
        sb.Append("  rm -f /tmp/out.txt\n");
        sb.Append("}\n");
        for (var i = 0; i < job.Cases.Count; i++)
        {
            var seconds = (job.Cases[i].TimeLimitMs / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
            sb.Append($"run_case {i} {seconds}\n");
        }
        return sb.ToString();
    }

    /// <summary>Parses the harness output; null when it never reported a compile result (the engine broke).</summary>
    public static RunReport? Parse(string stdout, CodeJob job, string engine, int durationMs)
    {
        bool? compiled = null;
        string? compileOutput = null;
        var cases = new List<CaseRun>();

        foreach (var raw in stdout.Replace("\r\n", "\n").Split('\n'))
        {
            var parts = raw.Split(' ');
            switch (parts[0])
            {
                case "COMPILED" or "COMPILE_FAILED":
                    compiled = parts[0] == "COMPILED";
                    compileOutput = parts.Length > 1 ? Decode(parts[1]) : null;
                    break;
                case "CASE" when parts.Length >= 5
                                 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var index)
                                 && index >= 0 && index < job.Cases.Count
                                 && int.TryParse(parts[2], CultureInfo.InvariantCulture, out var rc)
                                 && int.TryParse(parts[3], CultureInfo.InvariantCulture, out var ms):
                    var oom = parts[4] == "1";
                    var limit = job.Cases[index].TimeLimitMs;
                    // timeout(1) exits 124 on TERM, 137 if the program ignored TERM and was killed a second later.
                    // A 137 without the time being up and without a recorded OOM kill is still the OOM killer
                    // (cgroup v1 hosts have no memory.events).
                    var timedOut = rc == 124 || (rc == 137 && !oom && ms >= limit);
                    var memory = oom || (rc == 137 && !timedOut);
                    var output = parts.Length > 5 ? Decode(parts[5]) ?? string.Empty : string.Empty;
                    cases.Add(new CaseRun(index, output, timedOut ? null : rc, timedOut, memory && !timedOut, ms,
                        Encoding.UTF8.GetByteCount(output) >= job.Limits.MaxOutputBytes));
                    break;
            }
        }

        if (compiled is null) return null;
        var text = string.IsNullOrWhiteSpace(compileOutput) ? null : compileOutput.Trim();
        return new RunReport(compiled.Value, text, cases, engine, durationMs);
    }

    private static string? Decode(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64.Trim()));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>
/// Is the Docker sandbox usable right now (daemon up and the gcc image pulled)? Cached so the Auto runner choice costs
/// one <c>docker image inspect</c> per <c>Grading:Docker:ProbeCacheSeconds</c>, never a pull at grading time.
/// </summary>
public sealed class DockerProbe(IOptions<GradingOptions> options, ILogger<DockerProbe> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (bool Available, DateTime CheckedAt)? _cached;

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        var ttl = TimeSpan.FromSeconds(Math.Max(5, options.Value.Docker.ProbeCacheSeconds));
        if (_cached is { } c && DateTime.UtcNow - c.CheckedAt < ttl) return c.Available;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cached is { } again && DateTime.UtcNow - again.CheckedAt < ttl) return again.Available;
            var available = await CheckAsync(options.Value.Docker.Executable, options.Value.Docker.Image, cancellationToken);
            if (!available) logger.LogInformation("Docker sandbox unavailable (daemon down or image {Image} not pulled).", options.Value.Docker.Image);
            _cached = (available, DateTime.UtcNow);
            return available;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>True when <paramref name="executable"/> answers and <paramref name="image"/> exists locally.</summary>
    public static async Task<bool> CheckAsync(string executable, string image, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await GradingProcess.RunAsync(executable, ["image", "inspect", "--format", "{{.Id}}", image],
                TimeSpan.FromSeconds(20), cancellationToken);
            return !result.TimedOut && result.ExitCode == 0;
        }
        catch (GradingInfrastructureException)
        {
            return false;   // docker CLI not installed
        }
    }
}
