using System.Management;
using Student_Desktop_App.Core.Lockdown;
using Timer = System.Timers.Timer;

namespace Student_Desktop_App.Services.Lockdown;

/// <summary>
/// Watches the hardware/session environment: USB storage inserted, extra monitors
/// attached, and remote-desktop sessions. Each change is reported once.
/// </summary>
public sealed class DeviceGuardService : IDisposable
{
    private readonly bool _blockMultiMonitor;
    private readonly Timer _timer = new(5000) { AutoReset = true };
    private ManagementEventWatcher? _usbWatcher;
    private HashSet<string> _lastIssues = new();

    /// (reason, detail) e.g. ("UsbInserted", "E:"), ("MultiMonitor", "2"), ("RemoteSession", "")
    public event Action<string, string>? DeviceViolation;

    public DeviceGuardService(bool blockMultiMonitor)
    {
        _blockMultiMonitor = blockMultiMonitor;
        _timer.Elapsed += (_, _) => CheckEnvironment();
    }

    public static IReadOnlyList<string> CurrentIssues(bool blockMultiMonitor) =>
        LockdownPolicy.CheckEnvironment(
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CMONITORS),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_REMOTESESSION) != 0,
            blockMultiMonitor);

    public void Start()
    {
        CheckEnvironment();
        _timer.Start();
        try
        {
            // EventType 2 = device arrival
            _usbWatcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 2"));
            _usbWatcher.EventArrived += (_, e) =>
                DeviceViolation?.Invoke("UsbInserted", e.NewEvent["DriveName"]?.ToString() ?? "");
            _usbWatcher.Start();
        }
        catch
        {
            // WMI unavailable (locked-down image) — monitor/RDP checks still run.
            _usbWatcher = null;
        }
    }

    public void Stop()
    {
        _timer.Stop();
        _usbWatcher?.Stop();
        _usbWatcher?.Dispose();
        _usbWatcher = null;
    }

    private void CheckEnvironment()
    {
        var issues = CurrentIssues(_blockMultiMonitor).ToHashSet();
        foreach (var issue in issues.Except(_lastIssues))
        {
            var parts = issue.Split(':', 2);
            DeviceViolation?.Invoke(parts[0], parts.Length > 1 ? parts[1] : "");
        }
        _lastIssues = issues;
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }
}
