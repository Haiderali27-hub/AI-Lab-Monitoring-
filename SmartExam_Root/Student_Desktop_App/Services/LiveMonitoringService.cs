using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Student_Desktop_App.Core;
using Student_Desktop_App.Core.Lockdown;
using Student_Desktop_App.Core.Monitoring;
using Student_Desktop_App.Services.Backup;
using Student_Desktop_App.Services.Monitoring;
using Timer = System.Timers.Timer;

namespace Student_Desktop_App.Services;

/// <summary>
/// Module 5 — the live monitoring client. While an exam session runs it:
/// <list type="bullet">
/// <item>heartbeats every 10 s (foreground app by PID, visible apps, input idle seconds, webcam state) and follows
/// the server's clock, session status and capture settings from the ack;</item>
/// <item>queues every monitoring event (violations, focus loss, process changes, camera findings) in an encrypted
/// on-disk outbox (<see cref="MonitoringSpool"/>) and sends it in batches every 5 s — at once for a violation —
/// replaying the backlog in order after an outage, with the outage itself reported (<c>NetworkOffline</c>);</item>
/// <item>captures all screens on a random 30–90 s schedule and on every violation, and runs webcam monitoring when
/// the exam requires a webcam (<see cref="EvidenceCaptureService"/>, <see cref="WebcamMonitorService"/>);</item>
/// <item>Module 10: hands the proctors' warnings (<see cref="WarningInbox"/>) and the lockdown unlock deadline from
/// the ack to the exam window — the ack is this client's command channel, so both arrive within one heartbeat.</item>
/// </list>
/// Without a session (dashboard before the start) only the heartbeat runs.
/// </summary>
public class LiveMonitoringService(ApiClient apiClient)
{
    private const int MaxBatch = 100;
    private static readonly TimeSpan CaptureUploadInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ViolationCooldown = TimeSpan.FromSeconds(30);

    private readonly ApiClient _apiClient = apiClient;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private readonly object _gate = new();
    private Timer? _timer;
    private Timer? _tick;
    private string? _accessToken;
    private Guid? _examSessionId;
    private LockdownPolicy _focusPolicy = new(LockdownMode.Report, null, Process.GetCurrentProcess().ProcessName);
    private bool _wasForegroundExamApp = true;          // transition tracking for focus-loss violations
    private DateTime _lastViolationSentUtc = DateTime.MinValue;

    // Module 5 per-session evidence pipeline (null without a session, or when the local spool cannot be opened).
    private MonitoringSpool? _spool;
    private EventBatcher? _batcher;
    private EvidenceCaptureService? _captures;
    private WebcamMonitorService? _webcam;
    private CaptureSettings _capture = new();
    private IReadOnlyList<string>? _lastProcesses;
    private DateTime? _offlineSinceUtc;
    private DateTime _lastCaptureUpload = DateTime.MinValue;
    private Task? _finalising;
    // Module 10: proctor warnings of this session, and until when (this PC's clock) a proctor unlocked the lockdown.
    private WarningInbox _warnings = new();
    private DateTime? _unlockedUntilLocal;

    public event Action<string>? StatusChanged;
    public event Action<int>? RemainingSecondsUpdated;  // authoritative clock from the server ack
    public event Action? SessionRevoked;                // server force-logout / deactivation
    public event Action<string>? SessionStatusUpdated;  // Module 6: InProgress | Submitted | ForceSubmitted | TimedOut
    public event Action<IReadOnlyList<ProctorWarning>>? WarningsReceived;  // Module 10: warnings never shown before
    public event Action<DateTime?>? UnlockDeadlineReceived;                // Module 10: local unlock deadline, null = locked

    /// <summary>Module 10: this session's proctor warnings (the exam window shows <see cref="WarningInbox.Current"/>).</summary>
    public WarningInbox Warnings => _warnings;

    /// <summary>True while a proctor's unlock is in force (focus loss is then not reported).</summary>
    public bool IsUnlocked => _unlockedUntilLocal is DateTime until && until > DateTime.UtcNow;

    /// <param name="accessToken">Fallback only — ApiClient sends the current (refreshed) session token.</param>
    /// <param name="capture">The exam's capture settings (from student/current); the heartbeat ack keeps them current.</param>
    public void Start(string accessToken, Guid? examSessionId, IEnumerable<string>? allowedApps = null, CaptureSettings? capture = null)
    {
        _accessToken = accessToken;
        _focusPolicy = new LockdownPolicy(LockdownMode.Report, allowedApps, Process.GetCurrentProcess().ProcessName);
        _wasForegroundExamApp = true;
        _lastProcesses = null;
        _capture = capture ?? new CaptureSettings();

        if (_examSessionId != examSessionId)
        {
            StopEvidence();
            _warnings = new WarningInbox();
            _unlockedUntilLocal = null;
        }
        _examSessionId = examSessionId;
        if (examSessionId is Guid sid && _spool is null) StartEvidence(sid);

        if (_timer is null)
        {
            _timer = new Timer(AppConfigProvider.Current.HeartbeatSeconds * 1000D);
            // An async timer handler that throws would end the process (mid-exam, under lockdown): absorb and report.
            _timer.Elapsed += async (_, _) =>
            {
                try
                {
                    await SendHeartbeatAsync();
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke($"Heartbeat failed: {ex.Message}");
                }
            };
            _timer.AutoReset = true;
        }
        if (_tick is null)
        {
            _tick = new Timer(1000D);
            _tick.Elapsed += async (_, _) => await TickAsync();
            _tick.AutoReset = true;
        }

        _timer.Start();
        _tick.Start();
        StatusChanged?.Invoke(_captures is null
            ? "Monitoring started."
            : $"Monitoring started: screen captures every {_capture.ScreenshotIntervalSeconds / 2}–{_capture.ScreenshotIntervalSeconds * 3 / 2} s" +
              (_capture.RequireWebcam ? ", webcam checks on." : "."));
    }

    public void Stop()
    {
        _timer?.Stop();
        _tick?.Stop();
        StopEvidence();
        StatusChanged?.Invoke("Monitoring stopped.");
    }

    /// <summary>
    /// A violation found on this PC (lockdown guards, focus loss): queued for the proctor (sent within a second when
    /// online) and the screen is captured as evidence. Safe from any thread.
    /// </summary>
    public void ReportViolation(string reason, string detail)
    {
        var now = DateTime.UtcNow;
        if (!Enqueue("Violation", new { reason, detail, atUtc = now }, urgent: true))
        {
            // No local spool (profile problem): fall back to a direct, best-effort send.
            if (_accessToken is { } token && _examSessionId is Guid sid)
                _ = _apiClient.SendMonitoringEventAsync(token, "Violation", sid, new { reason, detail, atUtc = now });
            return;
        }
        _captures?.CaptureForViolation(reason);
        _ = FlushEventsAsync();
    }

    /// <summary>Any other monitoring event (e.g. BackupTampered), queued like a violation but not urgent unless asked.</summary>
    public void ReportEvent(string eventType, object payload, bool urgent = false)
    {
        if (!Enqueue(eventType, payload, urgent) && _accessToken is { } token && _examSessionId is Guid sid)
            _ = _apiClient.SendMonitoringEventAsync(token, eventType, sid, payload);
        else if (urgent)
            _ = FlushEventsAsync();
    }

    private bool Enqueue(string eventType, object payload, bool urgent)
    {
        var spool = _spool;
        if (spool is null) return false;
        try
        {
            spool.Events.Enqueue(new QueuedEvent(Guid.NewGuid(), eventType, JsonSerializer.Serialize(payload), DateTime.UtcNow, urgent));
            return true;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // ── per-session evidence pipeline ────────────────────────────────────────────────────────────

    private void StartEvidence(Guid sessionId)
    {
        // A previous stop of this same session may still be delivering its last events: let it finish first.
        _finalising?.Wait(TimeSpan.FromSeconds(3));
        try
        {
            var spool = MonitoringSpool.Open(MonitoringSpool.DefaultRoot, sessionId, new DpapiKeyProtector());
            lock (_gate)
            {
                _spool = spool;
                _batcher = new EventBatcher(() => DateTime.UtcNow);
                _captures = new EvidenceCaptureService(_apiClient, () => _accessToken ?? string.Empty, sessionId, spool.Captures,
                    _capture.ScreenshotIntervalSeconds);
                _captures.CaptureFailed += message => StatusChanged?.Invoke($"Screen capture failed: {message}");
            }
            if (spool.WasReset)
                ReportEvent("BackupTampered", new { reason = "MonitoringSpool", detail = "The local monitoring outbox failed its integrity check and was reset", atUtc = DateTime.UtcNow }, urgent: true);
            ApplyWebcamRequirement(_capture.RequireWebcam, _capture.WebcamIntervalSeconds);
        }
        catch (Exception ex)
        {
            // The exam still runs; events then go straight to the server and no captures are taken.
            StatusChanged?.Invoke($"Offline monitoring storage unavailable: {ex.Message}");
            lock (_gate)
            {
                _spool = null;
                _batcher = null;
                _captures = null;
            }
        }
    }

    private void StopEvidence()
    {
        WebcamMonitorService? webcam;
        MonitoringSpool? spool;
        EvidenceCaptureService? captures;
        lock (_gate)
        {
            webcam = _webcam;
            spool = _spool;
            captures = _captures;
            _webcam = null;
            _spool = null;
            _captures = null;
            _batcher = null;
        }
        webcam?.Stop();
        if (spool is null || _examSessionId is not Guid sid) return;

        // Deliver what is left (the exam just ended: its last violations and captures matter most), then remove
        // the spool if it is empty. Whatever cannot be delivered now stays on disk for the next run of this session.
        var token = _accessToken ?? string.Empty;
        _finalising = Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await DrainEventsAsync(spool, sid, token, cts.Token);
                if (captures is not null) await captures.UploadPendingAsync(cts.Token);
                if (spool.IsEmpty) spool.Delete();
            }
            catch (Exception)
            {
                // best effort; the spool stays for a later attempt or the 7-day cleanup
            }
        });
    }

    private void ApplyWebcamRequirement(bool required, int snapshotMeanSeconds)
    {
        lock (_gate)
        {
            if (required && _webcam is null && _captures is not null)
            {
                var captures = _captures;
                var webcam = new WebcamMonitorService(snapshotMeanSeconds);
                webcam.IssueChanged += t =>
                {
                    if (!t.Raised) return;   // the heartbeat's cameraStatus shows when it is fixed
                    Enqueue(t.Issue.ToString(), new { detail = t.Detail, atUtc = t.AtUtc }, urgent: true);
                    captures.CaptureForViolation(t.Issue.ToString());
                    _ = FlushEventsAsync();
                };
                webcam.SnapshotReady += (jpeg, trigger, reason) => captures.AddWebcamSnapshot(jpeg, trigger, reason);
                webcam.Start();
                _webcam = webcam;
            }
            else if (!required && _webcam is not null)
            {
                _webcam.Stop();
                _webcam = null;
            }
            else
            {
                _webcam?.UpdateSnapshotInterval(snapshotMeanSeconds);
            }
        }
    }

    private async Task TickAsync()
    {
        try
        {
            _captures?.Tick();
            await FlushEventsAsync();

            var captures = _captures;
            if (captures is not null && _offlineSinceUtc is null && DateTime.UtcNow - _lastCaptureUpload >= CaptureUploadInterval && captures.Pending > 0)
            {
                _lastCaptureUpload = DateTime.UtcNow;
                await captures.UploadPendingAsync();
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Monitoring tick failed: {ex.Message}");
        }
    }

    private async Task FlushEventsAsync(bool onlyWhenDue = true)
    {
        var spool = _spool;
        var batcher = _batcher;
        if (spool is null || batcher is null || _examSessionId is not Guid sid) return;
        lock (_gate)
        {
            if (onlyWhenDue && !batcher.IsDue(spool.Events.Count, spool.Events.HasUrgent)) return;
        }
        if (!await _flushLock.WaitAsync(0)) return;
        try
        {
            var delivered = await DrainEventsAsync(spool, sid, _accessToken ?? string.Empty, CancellationToken.None);
            lock (_gate)
            {
                if (delivered) batcher.MarkFlushed();
                else batcher.MarkFailed();
            }
        }
        finally
        {
            _flushLock.Release();
        }
    }

    // Sends the outbox in order, MaxBatch events at a time. False when the server could not be reached.
    private async Task<bool> DrainEventsAsync(MonitoringSpool spool, Guid sessionId, string token, CancellationToken cancellationToken)
    {
        while (true)
        {
            var batch = spool.Events.PeekBatch(MaxBatch);
            if (batch.Count == 0) return true;
            var outcome = await _apiClient.SendEventBatchAsync(token, sessionId, batch, cancellationToken);
            if (outcome == DeliveryOutcome.Offline) return false;
            // Delivered, or refused for good (e.g. a session that no longer exists): either way it leaves the outbox.
            spool.Events.Ack(batch.Select(e => e.ClientEventId));
        }
    }

    // ── heartbeat ────────────────────────────────────────────────────────────────────────────────

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
            // Decided by the owning process ID — a window merely titled "Exam" doesn't count.
            var foregroundPid = GetForegroundProcessId();
            var isForegroundExamApp = _focusPolicy.IsForegroundAcceptable(
                foregroundPid, Environment.ProcessId, ProcessNameOf(foregroundPid));

            var processSnapshot = GetProcessSnapshot();
            TrackProcessChanges(processSnapshot);

            var payload = new HeartbeatPayload(_examSessionId, isForegroundExamApp, activeWindowTitle, processSnapshot,
                GetIdleSeconds(), _webcam?.Status ?? "Off");

            var result = await _apiClient.SendHeartbeatAsync(_accessToken, payload);
            if (result.Success)
            {
                StatusChanged?.Invoke($"Heartbeat OK @ {DateTime.Now:T}");
                OnBackOnline();
                if (result.Data?.RemainingSeconds is int remaining)
                {
                    RemainingSecondsUpdated?.Invoke(remaining);
                }
                if (!string.IsNullOrEmpty(result.Data?.SessionStatus))
                {
                    SessionStatusUpdated?.Invoke(result.Data.SessionStatus);
                }
                ApplyServerCaptureSettings(result.Data);
                ApplyProctorCommands(result.Data);
            }
            else if (result.Code == "HTTP_401" && !await _apiClient.IsSessionAliveAsync(_accessToken))
            {
                // ApiClient already tried a token refresh; the session is confirmed dead
                // (force-logout / deactivation / revoked refresh token) — kick out.
                StatusChanged?.Invoke("Session ended by administrator.");
                SessionRevoked?.Invoke();
                return;
            }
            else
            {
                if (ApiClient.IsTransientFailure(result.Code)) _offlineSinceUtc ??= DateTime.UtcNow;
                StatusChanged?.Invoke($"Heartbeat failed: {result.Message}");
            }

            // Focus-loss violation: fire only on the transition INTO focus loss
            // (not every heartbeat), rate-limited so a distracted student doesn't
            // flood the teacher's dashboard.
            // Not while a proctor has unlocked the PC (the invigilator is using it on purpose).
            if (!isForegroundExamApp && _wasForegroundExamApp && _examSessionId.HasValue && !IsUnlocked &&
                DateTime.UtcNow - _lastViolationSentUtc > ViolationCooldown)
            {
                _lastViolationSentUtc = DateTime.UtcNow;
                ReportViolation("FocusLoss", activeWindowTitle);
                StatusChanged?.Invoke("Focus-loss violation reported.");
            }

            _wasForegroundExamApp = isForegroundExamApp;
        }
        finally
        {
            _lock.Release();
        }
    }

    // The network is back: report the outage window once and replay the backlog straight away.
    private void OnBackOnline()
    {
        if (_offlineSinceUtc is not DateTime from) return;
        _offlineSinceUtc = null;
        if (_examSessionId is null) return;
        Enqueue("NetworkOffline", new
        {
            offlineFromUtc = from,
            offlineToUtc = DateTime.UtcNow,
            queuedEvents = _spool?.Events.Count ?? 0,
            pendingCaptures = _captures?.Pending ?? 0
        }, urgent: false);
        lock (_gate) _batcher?.NetworkRestored();
        _lastCaptureUpload = DateTime.MinValue;
    }

    private void ApplyServerCaptureSettings(HeartbeatAck? ack)
    {
        if (ack is null || _captures is null) return;
        if (ack.ScreenshotIntervalSeconds is int screen) _captures.UpdateInterval(screen);
        if (ack.RequireWebcam is bool webcam)
            ApplyWebcamRequirement(webcam, ack.WebcamIntervalSeconds ?? _capture.WebcamIntervalSeconds);
    }

    // Module 10: warnings (each raised once; the server repeats them until acknowledged) and the unlock deadline,
    // converted to this PC's clock with the ack's own server time.
    private void ApplyProctorCommands(HeartbeatAck? ack)
    {
        if (ack is null || _examSessionId is null) return;

        var fresh = _warnings.Accept(ack.Warnings);
        if (fresh.Count > 0) WarningsReceived?.Invoke(fresh);

        _unlockedUntilLocal = LockdownSuspendPolicy.LocalDeadline(ack.UnlockedUntilUtc, ack.ReceivedAtUtc, DateTime.UtcNow);
        UnlockDeadlineReceived?.Invoke(_unlockedUntilLocal);
    }

    /// <summary>
    /// The student pressed Acknowledge on a warning banner: it leaves the banner, and the server is told so it stops
    /// sending it (best effort; offline, the inbox still remembers it was acknowledged).
    /// </summary>
    public async Task AcknowledgeWarningAsync(Guid notificationId)
    {
        _warnings.Acknowledge(notificationId);
        if (_accessToken is { } token) await _apiClient.MarkNotificationReadAsync(token, notificationId);
    }

    // A new or closed app becomes one timeline entry ("started chrome") instead of a full list every 10 s.
    private void TrackProcessChanges(string snapshot)
    {
        if (_examSessionId is null || snapshot == "UNAVAILABLE") return;
        var current = ProcessDiff.Parse(snapshot);
        var previous = _lastProcesses;
        _lastProcesses = current;
        if (previous is null) return;
        var change = ProcessDiff.Compute(previous, current);
        if (!change.IsEmpty)
            Enqueue("ProcessList", new { added = change.Added, removed = change.Removed, atUtc = DateTime.UtcNow }, urgent: false);
    }

    private static string GetProcessSnapshot()
    {
        try
        {
            // Apps with a visible window — what a proctor actually cares about (was: first 12 names).
            var processNames = Process.GetProcesses()
                .Where(x => x.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(x.ProcessName))
                .Select(x => x.ProcessName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToArray();

            return string.Join(',', processNames);
        }
        catch
        {
            return "UNAVAILABLE";
        }
    }

    // Seconds since the last keyboard/mouse input anywhere on the PC (a student who has not typed for minutes
    // during a coding exam is worth a look). Null when Windows will not say.
    private static int? GetIdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? IdleTime.Seconds(unchecked((uint)Environment.TickCount), info.dwTime) : null;
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

    private static int GetForegroundProcessId()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return 0;
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        return (int)pid;
    }

    private static string? ProcessNameOf(int pid)
    {
        if (pid == 0) return null;
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
}
