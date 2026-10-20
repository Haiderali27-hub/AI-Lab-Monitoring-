using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace Backend_API.Services.Grading;

/// <summary>
/// Module 11 — fallback runner when Docker is not available: compiles with the server's own g++ (MinGW on the lab
/// server: <c>C:\MinGW\bin</c>, put on the compiler's PATH) and runs the program as a local process. On Windows each run
/// is confined by a Job Object (<see cref="WindowsJobProcess"/>: one process, memory and CPU-time caps, no UI access,
/// killed on timeout). It has NO network and NO file-system isolation, so it is labelled "not isolated" everywhere it is
/// shown and is meant for development and demos; production servers should run Docker.
/// The program is linked statically (<c>-static</c>) so it runs without the MinGW DLLs, and compiled with
/// <c>-std=c++17 -O0</c> like the Docker runner (see there why unoptimised).
/// </summary>
public sealed class LocalMingwCodeRunner : ICodeRunner
{
    public const string DefaultMingwGpp = @"C:\MinGW\bin\g++.exe";

    private readonly GradingOptions _options;
    private readonly Lazy<string?> _gpp;

    public LocalMingwCodeRunner(IOptions<GradingOptions> options)
    {
        _options = options.Value;
        _gpp = new Lazy<string?>(() => FindGpp(_options.Local.GppPath));
    }

    public string Engine => OperatingSystem.IsWindows() ? "Local MinGW g++ (not isolated)" : "Local g++ (not isolated)";
    public bool Isolated => false;
    public bool IsAvailable => _gpp.Value is not null;
    public bool Supports(string language) => language is CodeLanguages.Cpp or CodeLanguages.C;

    /// <summary>Configured path, else g++ on PATH, else the default MinGW location on Windows; null when none exists.</summary>
    public static string? FindGpp(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;

        var exe = OperatingSystem.IsWindows() ? "g++.exe" : "g++";
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry.
            }
        }
        return OperatingSystem.IsWindows() && File.Exists(DefaultMingwGpp) ? DefaultMingwGpp : null;
    }

    public async Task<RunReport> RunAsync(CodeJob job, CancellationToken cancellationToken)
    {
        if (!Supports(job.Language))
            throw new GradingInfrastructureException($"The local runner does not support '{job.Language}'.");
        var gpp = _gpp.Value ?? throw new GradingInfrastructureException("No g++ found (set Grading:Local:GppPath or install MinGW).");
        var compilerDir = Path.GetDirectoryName(gpp)!;
        var compiler = job.Language == CodeLanguages.C
            ? Path.Combine(compilerDir, OperatingSystem.IsWindows() ? "gcc.exe" : "gcc")
            : gpp;

        var work = Path.Combine(Path.GetTempPath(), "smartexam-grading", Guid.NewGuid().ToString("N"));
        var watch = Stopwatch.StartNew();
        try
        {
            Directory.CreateDirectory(work);
            var sourceFile = job.Language == CodeLanguages.C ? "main.c" : "main.cpp";
            var exe = Path.Combine(work, OperatingSystem.IsWindows() ? "main.exe" : "main");
            await File.WriteAllTextAsync(Path.Combine(work, sourceFile), job.Source, new UTF8Encoding(false), cancellationToken);

            var args = job.Language == CodeLanguages.C
                ? new List<string> { "-std=c11", "-O0", "-static", "-o", exe, sourceFile, "-lm" }
                : new List<string> { "-std=c++17", "-O0", "-static", "-static-libgcc", "-static-libstdc++", "-o", exe, sourceFile };

            var compile = await GradingProcess.RunAsync(compiler, args, TimeSpan.FromSeconds(job.Limits.CompileTimeoutSeconds), cancellationToken,
                GradingProcess.ScrubbedEnvironment(work, compilerDir), work, maxOutputChars: AiOutputLimit);
            var compilerText = (compile.Stderr + compile.Stdout).Trim();
            if (compile.TimedOut)
                return new RunReport(false, $"Compilation timed out after {job.Limits.CompileTimeoutSeconds} s.", [], Engine, (int)watch.ElapsedMilliseconds);
            if (compile.ExitCode != 0 || !File.Exists(exe))
                return new RunReport(false, compilerText.Length == 0 ? $"Compiler exited with code {compile.ExitCode}." : compilerText,
                    [], Engine, (int)watch.ElapsedMilliseconds);

            var runEnv = GradingProcess.ScrubbedEnvironment(work);
            var cases = new List<CaseRun>();
            for (var i = 0; i < job.Cases.Count; i++)
            {
                var c = job.Cases[i];
                var input = GradingProcess.NormaliseInput(c.Input);
                if (OperatingSystem.IsWindows())
                {
                    WindowsJobProcess.Result r;
                    try
                    {
                        r = await WindowsJobProcess.RunAsync(exe, work, input, c.TimeLimitMs, job.Limits.MemoryLimitMb,
                            job.Limits.MaxOutputBytes, runEnv, cancellationToken);
                    }
                    catch (Win32Exception ex)
                    {
                        throw new GradingInfrastructureException($"Could not run the program: {ex.Message}", ex);
                    }
                    cases.Add(new CaseRun(i, r.Stdout, r.ExitCode, r.TimedOut, r.MemoryExceeded, r.DurationMs, r.OutputTruncated));
                }
                else
                {
                    cases.Add(await RunPlainAsync(i, exe, work, input, c.TimeLimitMs, job.Limits.MaxOutputBytes, runEnv, cancellationToken));
                }
            }

            return new RunReport(true, compilerText.Length == 0 ? null : compilerText, cases, Engine, (int)watch.ElapsedMilliseconds);
        }
        finally
        {
            await GradingProcess.DeleteDirectoryAsync(work);
        }
    }

    private const int AiOutputLimit = Models.AiGradingResult.MaxCompileOutputLength;

    // Non-Windows development machines: a plain child process with a wall-clock limit (no memory cap).
    private static async Task<CaseRun> RunPlainAsync(int index, string exe, string work, string input, int timeLimitMs, int maxOutput,
        IReadOnlyDictionary<string, string> env, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = work
        };
        psi.Environment.Clear();
        foreach (var (k, v) in env) psi.Environment[k] = v;

        using var process = new Process { StartInfo = psi };
        var watch = Stopwatch.StartNew();
        lock (GradingProcess.LaunchLock) process.Start();

        var stdoutTask = ReadCappedAsync(process.StandardOutput.BaseStream, maxOutput);
        _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null, cancellationToken);
        try
        {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }
        catch (IOException) { }

        var timedOut = false;
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            cts.CancelAfter(timeLimitMs);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                timedOut = !cancellationToken.IsCancellationRequested;
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        var (output, truncated) = await stdoutTask;
        return new CaseRun(index, output, timedOut ? null : process.ExitCode, timedOut, false, (int)watch.ElapsedMilliseconds, truncated);
    }

    private static async Task<(string, bool)> ReadCappedAsync(Stream stream, int maxBytes)
    {
        using var kept = new MemoryStream();
        var buffer = new byte[8192];
        var truncated = false;
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            var room = maxBytes - (int)kept.Length;
            if (room > 0) kept.Write(buffer, 0, Math.Min(room, read));
            if (read > room) truncated = true;
        }
        return (Encoding.UTF8.GetString(kept.ToArray()), truncated);
    }
}
