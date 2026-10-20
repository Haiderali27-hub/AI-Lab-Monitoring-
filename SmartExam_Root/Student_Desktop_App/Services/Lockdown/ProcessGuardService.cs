using System.Diagnostics;
using Student_Desktop_App.Core.Lockdown;
using Timer = System.Timers.Timer;

namespace Student_Desktop_App.Services.Lockdown;

/// <summary>
/// Polls the student's session every 2 s for unauthorized applications and reports
/// (Report mode) or terminates (Enforce mode) them according to <see cref="LockdownPolicy"/>.
/// </summary>
public sealed class ProcessGuardService : IDisposable
{
    private readonly LockdownPolicy _policy;
    private readonly Timer _timer = new(2000) { AutoReset = true };
    private readonly int _ownPid = Environment.ProcessId;
    private readonly int _sessionId = Process.GetCurrentProcess().SessionId;
    private readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);
    private int _scanning;

    /// (processName, action) where action is "Reported" or "Killed" / "KillFailed".
    public event Action<string, string>? UnauthorizedProcess;

    public ProcessGuardService(LockdownPolicy policy)
    {
        _policy = policy;
        _timer.Elapsed += (_, _) => Scan();
    }

    public void Start()
    {
        Scan();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void Scan()
    {
        if (Interlocked.Exchange(ref _scanning, 1) == 1) return;
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.Id == _ownPid || process.SessionId != _sessionId) continue;

                        var decision = _policy.Evaluate(process.ProcessName, process.MainWindowHandle != IntPtr.Zero);
                        if (decision == ProcessDecision.Allowed) continue;

                        if (decision == ProcessDecision.Kill)
                        {
                            var action = TryKill(process) ? "Killed" : "KillFailed";
                            UnauthorizedProcess?.Invoke(process.ProcessName, action);
                        }
                        else if (_reported.Add(process.ProcessName))
                        {
                            // Report mode: tell the proctor once per app, not every 2 s.
                            UnauthorizedProcess?.Invoke(process.ProcessName, "Reported");
                        }
                    }
                    catch
                    {
                        // Process exited mid-scan or access denied (elevated process) — skip it.
                    }
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _scanning, 0);
        }
    }

    private static bool TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
