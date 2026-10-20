using System.Collections.Concurrent;
using System.Windows;
using Student_Desktop_App.Core.Lockdown;

namespace Student_Desktop_App.Services.Lockdown;

/// <summary>
/// Starts and stops every lockdown guard together with an exam session and forwards
/// their findings to the proctor as Violation events. Teardown is idempotent and also
/// runs on unhandled exceptions and process exit so the student's PC is never left hooked
/// (and the Task Manager policy is restored; a hard kill is recovered at the next app start).
/// </summary>
public sealed class LockdownCoordinator : IDisposable
{
    // Same reason+detail is reported at most once per window (e.g. a student hammering Alt+Tab).
    private static readonly TimeSpan ReportCooldown = TimeSpan.FromSeconds(15);

    private readonly ApiClient _apiClient;
    private readonly string _accessToken;
    private readonly Guid _sessionId;
    private readonly KeyboardHookService _keyboard;
    private readonly ProcessGuardService _processes;
    private readonly ClipboardGuardService _clipboard = new();
    private readonly DeviceGuardService _devices;
    private readonly TaskManagerGuard? _taskManager;   // null when the policy is off for this exam
    private readonly ConcurrentDictionary<string, DateTime> _lastReported = new();
    private bool _active;

    public LockdownPolicy Policy { get; }
    public bool IsActive => _active;

    /// Raised for every violation (UI thread not guaranteed) — used for the on-screen warning.
    public event Action<string>? ViolationRaised;

    /// <param name="accessToken">Fallback only — ApiClient sends the current (refreshed) session token.</param>
    /// <param name="disableTaskManager">Config switch for the DisableTaskMgr policy (see TaskManagerPolicy).</param>
    public LockdownCoordinator(ApiClient apiClient, string accessToken, Guid sessionId,
        LockdownMode mode, IEnumerable<string>? allowedApps, bool blockMultiMonitor, bool disableTaskManager = true)
    {
        _apiClient = apiClient;
        _accessToken = accessToken;
        _sessionId = sessionId;
        Policy = new LockdownPolicy(mode, allowedApps, System.Diagnostics.Process.GetCurrentProcess().ProcessName);

        _keyboard = new KeyboardHookService(Policy);
        _processes = new ProcessGuardService(Policy);
        _devices = new DeviceGuardService(blockMultiMonitor);
        if (TaskManagerPolicy.ShouldDisable(mode, disableTaskManager))
            _taskManager = TaskManagerRegistryGuard.Create();

        _keyboard.KeyBlocked += keys => Report("KeyBlocked", keys);
        _processes.UnauthorizedProcess += (name, action) =>
            Report(action == "Killed" ? "ProcessKilled" : "UnauthorizedProcess", $"{name} ({action})");
        _clipboard.ExternalClipboardBlocked += owner => Report("ClipboardBlocked", owner);
        _devices.DeviceViolation += Report;
    }

    /// Must be called on the UI thread (keyboard hook + clipboard listener need its message loop).
    public void Start(Window examWindow)
    {
        if (_active || Policy.Mode == LockdownMode.Off) return;
        _active = true;

        AppDomain.CurrentDomain.UnhandledException += OnCrash;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Application.Current.DispatcherUnhandledException += OnDispatcherCrash;

        if (_taskManager is not null)
        {
            var applied = _taskManager.Apply();
            if (applied is TaskManagerApplyResult.MarkerWriteFailed or TaskManagerApplyResult.RegistryFailed)
                Report("LockdownDegraded", $"Task Manager policy not applied ({applied})");
        }

        if (!_keyboard.Install())
            Report("LockdownDegraded", "Keyboard hook could not be installed");
        _clipboard.Start(examWindow);
        _processes.Start();
        _devices.Start();
    }

    public void Stop()
    {
        if (!_active) return;
        _active = false;

        // Stop() also runs from the crash / process-exit handlers on non-UI threads, where a UI-bound step
        // can throw. Restore the Task Manager policy first and isolate every step, so nothing is left behind.
        LockdownTeardown.RunAll(
            ("taskmgr-policy", () => _taskManager?.Restore()),
            ("keyboard-hook", _keyboard.Uninstall),
            ("clipboard", _clipboard.Stop),
            ("process-guard", _processes.Stop),
            ("device-guard", _devices.Stop),
            ("handlers", () =>
            {
                AppDomain.CurrentDomain.UnhandledException -= OnCrash;
                AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
                if (Application.Current is not null)
                    Application.Current.DispatcherUnhandledException -= OnDispatcherCrash;
            }));
    }

    private void Report(string reason, string detail)
    {
        var key = $"{reason}|{detail}";
        var now = DateTime.UtcNow;
        if (_lastReported.TryGetValue(key, out var last) && now - last < ReportCooldown) return;
        _lastReported[key] = now;

        ViolationRaised?.Invoke(reason);

        // Fire-and-forget: a slow network must never stall the hook or the UI.
        _ = _apiClient.SendMonitoringEventAsync(_accessToken, "Violation", _sessionId,
            new { reason, detail, atUtc = now });
    }

    private void OnCrash(object sender, UnhandledExceptionEventArgs e) => Stop();

    // Environment.Exit / normal shutdown with the exam window still up: never leave the policy behind.
    private void OnProcessExit(object? sender, EventArgs e) => Stop();

    private void OnDispatcherCrash(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) => Stop();

    public void Dispose()
    {
        Stop();
        _keyboard.Dispose();
        _processes.Dispose();
        _devices.Dispose();
    }
}
