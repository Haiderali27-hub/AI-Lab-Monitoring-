using System.Diagnostics;
using System.Text;

namespace Backend_API.Services.Grading;

/// <summary>
/// Starts the trusted helper processes of the grading engine (the docker CLI, g++) with a hard timeout, capped output
/// and, when asked, a scrubbed environment — student code must never see the server's environment variables
/// (connection strings, API keys). All grading launches share <see cref="LaunchLock"/> with
/// <see cref="WindowsJobProcess"/>, whose inheritable pipe handles would otherwise leak into a child started at the
/// same moment and keep its pipes open.
/// </summary>
internal static class GradingProcess
{
    internal static readonly object LaunchLock = new();

    public sealed record Result(int ExitCode, string Stdout, string Stderr, bool TimedOut);

    public static async Task<Result> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null,
        string? workingDirectory = null,
        int maxOutputChars = 8 * 1024 * 1024,
        Func<Task>? onTimeout = null)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory ?? string.Empty
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);
        if (environment is not null)
        {
            psi.Environment.Clear();
            foreach (var (k, v) in environment) psi.Environment[k] = v;
        }

        using var process = new Process { StartInfo = psi };
        lock (LaunchLock)
        {
            try
            {
                process.Start();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                throw new GradingInfrastructureException($"Could not start '{fileName}': {ex.Message}", ex);
            }
        }

        var stdout = ReadCappedAsync(process.StandardOutput, maxOutputChars);
        var stderr = ReadCappedAsync(process.StandardError, maxOutputChars);

        var timedOut = false;
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            cts.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                timedOut = !cancellationToken.IsCancellationRequested;
                if (onTimeout is not null)
                {
                    try { await onTimeout(); } catch { /* best effort: the kill below still runs */ }
                }
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        return new Result(process.ExitCode, await stdout, await stderr, timedOut);
    }

    private static async Task<string> ReadCappedAsync(StreamReader reader, int maxChars)
    {
        var sb = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            // Keep draining after the cap so the child never blocks on a full pipe.
            var room = maxChars - sb.Length;
            if (room > 0) sb.Append(buffer, 0, Math.Min(room, read));
        }
        return sb.ToString();
    }

    /// <summary>Minimal environment for compilers and student programs: system paths only, temp in the work folder.</summary>
    public static Dictionary<string, string> ScrubbedEnvironment(string workDir, string? extraPath = null)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (OperatingSystem.IsWindows())
        {
            var root = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            env["SystemRoot"] = root;
            env["SystemDrive"] = Path.GetPathRoot(root)?.TrimEnd('\\') ?? "C:";
            env["PATH"] = string.Join(';', new[] { extraPath, Path.Combine(root, "System32") }.Where(p => !string.IsNullOrEmpty(p))!);
        }
        else
        {
            env["PATH"] = string.Join(':', new[] { extraPath, "/usr/local/bin", "/usr/bin", "/bin" }.Where(p => !string.IsNullOrEmpty(p))!);
            env["HOME"] = workDir;
        }
        env["TEMP"] = workDir;
        env["TMP"] = workDir;
        env["TMPDIR"] = workDir;
        return env;
    }

    /// <summary>Deletes a grading work folder; a file still locked by a dying process is retried, then left for the OS.</summary>
    public static async Task DeleteDirectoryAsync(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(200);
            }
        }
    }

    /// <summary>Program input as the sandbox writes it: LF line endings and a final newline (line readers expect one).</summary>
    public static string NormaliseInput(string? input)
    {
        var text = (input ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        return text.Length == 0 || text.EndsWith('\n') ? text : text + "\n";
    }
}
