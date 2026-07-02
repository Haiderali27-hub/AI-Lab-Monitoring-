using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Student_Desktop_App.Core;
using Student_Desktop_App.Services;
using Timer = System.Timers.Timer;

namespace Student_Desktop_App.Services;

public class LiveMonitoringService(ApiClient apiClient)
{
    private readonly ApiClient _apiClient = apiClient;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Timer? _timer;
    private string? _accessToken;
    private Guid? _examSessionId;
    private bool _wasForegroundExamApp = true;          // transition tracking for focus-loss violations
    private DateTime _lastViolationSentUtc = DateTime.MinValue;
    private static readonly TimeSpan ViolationCooldown = TimeSpan.FromSeconds(30);

    public event Action<string>? StatusChanged;
    public event Action<int>? RemainingSecondsUpdated;  // authoritative clock from the server ack
    public event Action? SessionRevoked;                // server force-logout / deactivation

    public void Start(string accessToken, Guid? examSessionId)
    {
        _accessToken = accessToken;
        _examSessionId = examSessionId;

        if (_timer is null)
        {
            _timer = new Timer(AppConfigProvider.Current.HeartbeatSeconds * 1000D);
            _timer.Elapsed += async (_, _) => await SendHeartbeatAsync();
            _timer.AutoReset = true;
        }

        _timer.Start();
        StatusChanged?.Invoke("Monitoring started.");
    }

    public void Stop()
    {
        if (_timer is null)
        {
            return;
        }

        _timer.Stop();
        StatusChanged?.Invoke("Monitoring stopped.");
    }

    private async Task SendHeartbeatAsync()
    {
        if (string.IsNullOrWhiteSpace(_accessToken))
        {
            return;
        }

        if (!await _lock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var activeWindowTitle = GetActiveWindowTitle();
            var isForegroundExamApp = activeWindowTitle.Contains("Exam", StringComparison.OrdinalIgnoreCase) ||
                                      activeWindowTitle.Contains("Smart", StringComparison.OrdinalIgnoreCase);

            var processSnapshot = GetProcessSnapshot();
            var payload = new HeartbeatPayload(_examSessionId, isForegroundExamApp, activeWindowTitle, processSnapshot);

            var result = await _apiClient.SendHeartbeatAsync(_accessToken, payload);
            if (result.Success)
            {
                StatusChanged?.Invoke($"Heartbeat OK @ {DateTime.Now:T}");
                if (result.Data?.RemainingSeconds is int remaining)
                {
                    RemainingSecondsUpdated?.Invoke(remaining);
                }
            }
            else if (result.Code == "HTTP_401")
            {
                // Server revoked this session (force-logout / deactivation) — kick out.
                StatusChanged?.Invoke("Session ended by administrator.");
                SessionRevoked?.Invoke();
                return;
            }
            else
            {
                StatusChanged?.Invoke($"Heartbeat failed: {result.Message}");
            }

            // Focus-loss violation: fire only on the transition INTO focus loss
            // (not every heartbeat), rate-limited so a distracted student doesn't
            // flood the teacher's dashboard.
            if (!isForegroundExamApp && _wasForegroundExamApp && _examSessionId.HasValue &&
                DateTime.UtcNow - _lastViolationSentUtc > ViolationCooldown)
            {
                _lastViolationSentUtc = DateTime.UtcNow;
                var violationPayload = new
                {
                    reason = "FocusLoss",
                    activeWindow = activeWindowTitle,
                    atUtc = DateTime.UtcNow
                };
                await _apiClient.SendMonitoringEventAsync(_accessToken, "Violation", _examSessionId, violationPayload);
                StatusChanged?.Invoke("Focus-loss violation reported.");
            }

            _wasForegroundExamApp = isForegroundExamApp;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string GetProcessSnapshot()
    {
        try
        {
            var processNames = Process.GetProcesses()
                .Where(x => !string.IsNullOrWhiteSpace(x.ProcessName))
                .OrderBy(x => x.ProcessName)
                .Take(12)
                .Select(x => x.ProcessName)
                .ToArray();

            return string.Join(',', processNames);
        }
        catch
        {
            return "UNAVAILABLE";
        }
    }

    private static string GetActiveWindowTitle()
    {
        var windowHandle = GetForegroundWindow();
        if (windowHandle == IntPtr.Zero)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(512);
        _ = GetWindowText(windowHandle, builder, builder.Capacity);
        return builder.ToString();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
}