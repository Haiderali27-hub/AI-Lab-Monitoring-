using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Threading;
using Student_Desktop_App.Core;
using Student_Desktop_App.Core.Backup;
using Student_Desktop_App.Core.Dashboard;
using Student_Desktop_App.Core.Readiness;
using Student_Desktop_App.Services;
using Student_Desktop_App.Services.Readiness;

namespace Student_Desktop_App.ViewModels;

public class DashboardViewModel(ApiClient apiClient, AnswerBackupStore? backupStore = null) : BindableBase
{
    private readonly ApiClient _apiClient = apiClient;
    private readonly AnswerBackupStore? _backupStore = backupStore;
    private readonly ReadinessCheckService _readiness = new(apiClient);
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private string _studentDisplayName = "Student";
    private string _studentInitials = "S";
    private string _examName = "No active exam";
    private string _examStatus = "NotStarted";
    private string _remainingTime = "00:00:00";
    private string _monitoringStatus = "Monitoring inactive";
    private string _message = "Waiting for exam eligibility check...";
    private string _instructions = "";
    private string _labName = "";
    private string _proctorName = "";
    private string _seat = "Unassigned";
    private string _connectionStatus = "Connecting…";
    private bool _isConnected;
    private string _attendanceStatus = "Not checked in";
    private string _readinessStatus = "Not run";
    private bool _attendanceConfirmed;
    private bool _readinessPassed;
    private bool _isCheckingReadiness;
    private bool _requireReadiness;
    private ReadinessRequirements _requirements = new(false, false, true, Core.Lockdown.LockdownMode.Report);
    private bool _canStartExam;
    private bool _canResumeExam;
    private string _resumeNotice = "";
    private bool _examInProgress;
    private bool _lowTime;
    private string _countdownLabel = "TIME REMAINING";
    // Module 3 start gate, evaluated on the server clock (see Core/Dashboard/StartGate).
    private StartGateInput? _gate;
    private TimeSpan _clockOffset;
    private bool _wasOpen;
    private int _remainingSeconds;
    private int _unreadNotifications;
    private Guid? _currentExamId;

    public Guid? ExamSessionId { get; private set; }
    public Guid? CurrentExamId => _currentExamId;
    public int RemainingSecondsSnapshot => _remainingSeconds;

    // Allowed applications shown as chips; teacher warnings shown in the notification rail.
    public ObservableCollection<string> AllowedApps { get; } = new();
    // Raw executable names for the lockdown whitelist (AllowedApps may hold a display placeholder).
    public List<string> AllowedAppNames { get; } = new();
    // Per-exam lockdown settings from the server (null → fall back to local config).
    public string? ExamLockdownMode { get; private set; }
    public bool? ExamBlockMultiMonitor { get; private set; }
    // Module 5: capture settings of the current exam (screen/webcam intervals, webcam required).
    public CaptureSettings ExamCapture { get; private set; } = new();
    public ObservableCollection<NotificationItem> Notifications { get; } = new();
    // Module 2 pre-exam checklist (one row per check, each retryable).
    public ObservableCollection<ReadinessRow> ReadinessItems { get; } = new();
    // Practice (mock) exams the student can rehearse with.
    public ObservableCollection<PracticeExamSummary> PracticeExams { get; } = new();
    public bool HasPracticeExams => PracticeExams.Count > 0;
    public bool HasReadinessItems => ReadinessItems.Count > 0;

    public string StudentDisplayName
    {
        get => _studentDisplayName;
        set => SetProperty(ref _studentDisplayName, value);
    }

    public string StudentInitials
    {
        get => _studentInitials;
        set => SetProperty(ref _studentInitials, value);
    }

    public string Seat
    {
        get => _seat;
        set => SetProperty(ref _seat, value);
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set => SetProperty(ref _connectionStatus, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    public bool ExamInProgress
    {
        get => _examInProgress;
        set => SetProperty(ref _examInProgress, value);
    }

    public bool LowTime
    {
        get => _lowTime;
        set => SetProperty(ref _lowTime, value);
    }

    public int UnreadNotifications
    {
        get => _unreadNotifications;
        set { if (SetProperty(ref _unreadNotifications, value)) OnPropertyChanged(nameof(HasUnread)); }
    }

    public bool HasUnread => _unreadNotifications > 0;

    public string ExamName
    {
        get => _examName;
        set => SetProperty(ref _examName, value);
    }

    public string ExamStatus
    {
        get => _examStatus;
        set => SetProperty(ref _examStatus, value);
    }

    public string RemainingTime
    {
        get => _remainingTime;
        set => SetProperty(ref _remainingTime, value);
    }

    // "TIME REMAINING" once the exam is open, "STARTS IN" before (RemainingTime then counts to the opening).
    public string CountdownLabel
    {
        get => _countdownLabel;
        set => SetProperty(ref _countdownLabel, value);
    }

    public string MonitoringStatus
    {
        get => _monitoringStatus;
        set => SetProperty(ref _monitoringStatus, value);
    }

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public string Instructions
    {
        get => _instructions;
        set => SetProperty(ref _instructions, value);
    }

    public string LabName
    {
        get => _labName;
        set => SetProperty(ref _labName, value);
    }

    public string ProctorName
    {
        get => _proctorName;
        set => SetProperty(ref _proctorName, value);
    }

    public string AttendanceStatus
    {
        get => _attendanceStatus;
        set => SetProperty(ref _attendanceStatus, value);
    }

    public string ReadinessStatus
    {
        get => _readinessStatus;
        set => SetProperty(ref _readinessStatus, value);
    }

    public bool AttendanceConfirmed
    {
        get => _attendanceConfirmed;
        set => SetProperty(ref _attendanceConfirmed, value);
    }

    public bool ReadinessPassed
    {
        get => _readinessPassed;
        set => SetProperty(ref _readinessPassed, value);
    }

    public bool IsCheckingReadiness
    {
        get => _isCheckingReadiness;
        set => SetProperty(ref _isCheckingReadiness, value);
    }

    // True when this exam's server-side start gate needs a passing check.
    public bool RequireReadiness
    {
        get => _requireReadiness;
        set => SetProperty(ref _requireReadiness, value);
    }

    public bool CanStartExam
    {
        get => _canStartExam;
        set => SetProperty(ref _canStartExam, value);
    }

    // Module 6: the exam is already running on the server (crash, power cut, relaunch) → "Resume exam".
    public bool CanResumeExam
    {
        get => _canResumeExam;
        private set { if (SetProperty(ref _canResumeExam, value)) OnPropertyChanged(nameof(StartButtonLabel)); }
    }

    public string StartButtonLabel => CanResumeExam ? "Resume Exam" : "Start Exam";

    /// <summary>Shown when answers from an unfinished exam are saved on this PC (encrypted backup).</summary>
    public string ResumeNotice
    {
        get => _resumeNotice;
        private set { if (SetProperty(ref _resumeNotice, value)) OnPropertyChanged(nameof(HasResumeNotice)); }
    }

    public bool HasResumeNotice => !string.IsNullOrEmpty(_resumeNotice);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var token = SessionStore.CurrentToken;
        if (token is null)
        {
            Message = "Session not found. Please login again.";
            return;
        }

        StudentDisplayName = token.User.Username;
        StudentInitials = ComputeInitials(token.User.Username);

        var result = await _apiClient.GetCurrentExamStatusAsync(token.AccessToken, cancellationToken);
        if (!result.Success || result.Data is null)
        {
            Message = result.Message;
            CanStartExam = false;
            IsConnected = result.Code != "NETWORK_ERROR";
            ConnectionStatus = IsConnected ? "Connected to exam server" : "Server unreachable";
            return;
        }

        IsConnected = true;
        ConnectionStatus = "Connected to exam server";
        ApplyExamStatus(result.Data);
        RefreshResumeNotice(token.User.Id);
        await RefreshNotificationsAsync(cancellationToken);
        await RefreshPracticeExamsAsync(cancellationToken);
    }

    public async Task RefreshPracticeExamsAsync(CancellationToken cancellationToken = default)
    {
        var token = SessionStore.CurrentToken;
        if (token is null) return;

        var result = await _apiClient.GetPracticeExamsAsync(token.AccessToken, cancellationToken);
        PracticeExams.Clear();
        if (result.Success && result.Data is not null)
            foreach (var p in result.Data) PracticeExams.Add(p);
        OnPropertyChanged(nameof(HasPracticeExams));
    }

    private static string ComputeInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "S";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[^1][0])}"
            : char.ToUpper(parts[0][0]).ToString();
    }

    // Poll teacher warnings/announcements; called on load and by the dashboard's watch timer.
    public async Task RefreshNotificationsAsync(CancellationToken cancellationToken = default)
    {
        var token = SessionStore.CurrentToken;
        if (token is null) return;

        var data = await _apiClient.GetNotificationsAsync(token.AccessToken, cancellationToken);
        if (data is null) return;

        Notifications.Clear();
        foreach (var n in data.Notifications.Take(20))
        {
            Notifications.Add(n);
        }
        UnreadNotifications = data.UnreadCount;
        OnPropertyChanged(nameof(HasNotifications));
    }

    public bool HasNotifications => Notifications.Count > 0;

    // Crash recovery hint: an unsubmitted local backup of this student's running exam.
    private void RefreshResumeNotice(Guid userId)
    {
        if (_backupStore is null || !ExamInProgress)
        {
            ResumeNotice = ExamInProgress ? "Your exam is still running. Press Resume Exam to continue where you left off." : "";
            return;
        }

        IReadOnlyList<BackupState> pending;
        try
        {
            pending = _backupStore.FindResumable(userId);
        }
        catch (Exception)
        {
            pending = Array.Empty<BackupState>();
        }
        var match = pending.FirstOrDefault(b => b.ExamId == _currentExamId);
        ResumeNotice = match is null
            ? "Your exam is still running. Press Resume Exam to continue where you left off."
            : $"Your exam is still running. {match.Answers.Count} answer(s) are saved on this PC" +
              (match.UnsentCount > 0 ? $" ({match.UnsentCount} not yet on the server — they are sent when you resume)." : ".") +
              " Press Resume Exam to continue.";
    }

    public async Task<bool> StartExamAsync(CancellationToken cancellationToken = default)
    {
        var token = SessionStore.CurrentToken;
        if (token is null)
        {
            Message = "Session missing. Please login again.";
            return false;
        }

        // Resuming a running session needs no new check-in or readiness check (the server returns the same session).
        if (!CanResumeExam && (!AttendanceConfirmed || (RequireReadiness && !ReadinessPassed)))
        {
            Message = RequireReadiness
                ? "Check in and pass the system check before starting the exam."
                : "Check in before starting the exam.";
            return false;
        }

        var result = await _apiClient.StartExamAsync(token.AccessToken, cancellationToken);
        if (!result.Success || result.Data is null)
        {
            // Server messages are authoritative: EXAM_NOT_STARTED (our clock was off), SEAT_MISMATCH
            // ("This exam must be taken from seat R1C1 …"), NOT_ELIGIBLE, EXAM_CLOSED, READINESS_REQUIRED.
            Message = result.Message;
            if (result.Code == "SEAT_MISMATCH" && _gate is not null)
            {
                _gate = _gate with { SeatStatus = "Mismatch" };
                UpdateStartEligibility();
            }
            if (result.Code == "READINESS_REQUIRED")
            {
                // Server says our last pass is missing or expired (> 30 min) — run it again.
                ReadinessPassed = false;
                ReadinessStatus = "Expired — run the check again";
                UpdateStartEligibility();
            }
            return false;
        }

        ExamSessionId = result.Data.Id;
        ExamStatus = result.Data.Status;
        CanStartExam = false;
        Message = CanResumeExam ? "Exam session resumed." : "Exam session started.";
        return true;
    }

    public async Task<bool> ConfirmAttendanceAsync(CancellationToken cancellationToken = default)
    {
        var token = SessionStore.CurrentToken;
        if (token is null || _currentExamId is null)
        {
            Message = "Exam session missing. Refresh and try again.";
            return false;
        }

        var payload = new { examId = _currentExamId, atUtc = DateTime.UtcNow };
        var result = await _apiClient.SendMonitoringEventAsync(token.AccessToken, "Attendance", null, payload, cancellationToken);
        if (!result.Success)
        {
            Message = result.Message;
            return false;
        }

        AttendanceConfirmed = true;
        AttendanceStatus = "Checked in";
        UpdateStartEligibility();
        return true;
    }

    /// <summary>
    /// Module 2: runs every workstation check, shows each row, and reports the result to the
    /// server (which decides whether start unlocks). Replaces the old "Verify identity" button,
    /// which only logged an event — face recognition is out of scope (thesis Ch-1-4).
    /// </summary>
    public async Task<bool> RunReadinessAsync(CancellationToken cancellationToken = default)
    {
        if (_currentExamId is null)
        {
            Message = "No exam to check for. Refresh and try again.";
            return false;
        }

        IsCheckingReadiness = true;
        ReadinessStatus = "Checking…";
        ReadinessItems.Clear();
        foreach (var check in ReadinessPolicy.AllChecks)
            ReadinessItems.Add(new ReadinessRow(check) { IsRunning = true });
        OnPropertyChanged(nameof(HasReadinessItems));

        try
        {
            foreach (var row in ReadinessItems)
            {
                row.Apply(await _readiness.RunAsync(row.Name, _requirements, cancellationToken));
            }
            return await ReportReadinessAsync(cancellationToken);
        }
        finally
        {
            IsCheckingReadiness = false;
        }
    }

    /// <summary>Re-runs one failed check, then re-reports the whole checklist.</summary>
    public async Task<bool> RetryReadinessItemAsync(string check, CancellationToken cancellationToken = default)
    {
        var row = ReadinessItems.FirstOrDefault(r => r.Name == check);
        if (row is null || IsCheckingReadiness) return false;

        IsCheckingReadiness = true;
        row.IsRunning = true;
        try
        {
            row.Apply(await _readiness.RunAsync(check, _requirements, cancellationToken));
            return await ReportReadinessAsync(cancellationToken);
        }
        finally
        {
            IsCheckingReadiness = false;
        }
    }

    private async Task<bool> ReportReadinessAsync(CancellationToken cancellationToken)
    {
        var results = ReadinessItems.Select(r => r.Result!).ToList();
        var localPass = ReadinessPolicy.PassedAll(results);

        var token = SessionStore.CurrentToken;
        if (token is null || _currentExamId is null)
        {
            Message = "Session missing. Please login again.";
            return false;
        }

        var payload = new
        {
            passedAll = localPass,
            machineName = Environment.MachineName,
            results = results.Select(r => new { name = r.Name, passed = r.Passed, detail = r.Detail, durationMs = r.DurationMs })
        };
        var (ok, serverPass, message) = await _apiClient.SubmitReadinessAsync(token.AccessToken, _currentExamId.Value, payload, cancellationToken);

        ReadinessPassed = ok && serverPass;
        var failed = results.Count(r => !r.Passed);
        ReadinessStatus = !ok ? "Could not reach server"
            : ReadinessPassed ? "All checks passed"
            : failed > 0 ? $"{failed} check(s) failed"
            : "Rejected by server — a required device was not checked";
        if (!ok) Message = message;
        UpdateStartEligibility();
        return ReadinessPassed;
    }

    public void UpdateMonitoringStatus(string status)
    {
        MonitoringStatus = status;
    }

    // Resync the countdown with the server's authoritative clock (heartbeat ack) —
    // this is how a teacher-granted time extension reaches a running client.
    public void UpdateRemainingSeconds(int seconds)
    {
        _remainingSeconds = Math.Max(0, seconds);
        if (_gate is not null) _gate = _gate with { EndUtc = StartGate.ServerNow(_clockOffset, DateTime.UtcNow).AddSeconds(_remainingSeconds) };
        RefreshCountdownDisplay();
        if (_remainingSeconds > 0 && !_countdownTimer.IsEnabled)
        {
            _countdownTimer.Start();
        }
    }

    private void ApplyExamStatus(StudentExamStatus status)
    {
        _currentExamId = status.ExamId;
        ExamName = status.ExamName;
        ExamStatus = status.Status;
        Message = status.Message;
        Instructions = status.Instructions ?? "No instructions provided.";
        LabName = status.LabName ?? "Unassigned";
        ProctorName = status.ProctorName ?? "Not assigned";
        Seat = status.Seat ?? "Unassigned";
        _remainingSeconds = Math.Max(0, status.RemainingSeconds);
        _clockOffset = StartGate.ClockOffset(status.ServerTimeUtc, DateTime.UtcNow);
        _gate = new StartGateInput(
            status.IsEligible,
            status.Status,
            StartGate.OpensAt(status.ExamStartUtc, status.EarlyJoinMinutes, status.OpensAtUtc),
            status.ExamEndUtc,
            status.SeatStatus);
        _wasOpen = StartGate.HasOpened(_gate.OpensAtUtc, _clockOffset, DateTime.UtcNow);
        RefreshCountdownDisplay();
        ExamInProgress = string.Equals(status.Status, "InProgress", StringComparison.OrdinalIgnoreCase);

        // Parse the allowed-apps JSON array into chips
        AllowedApps.Clear();
        AllowedAppNames.Clear();
        ExamLockdownMode = status.LockdownMode;
        ExamBlockMultiMonitor = status.BlockMultiMonitor;
        ExamCapture = new CaptureSettings(status.ScreenshotIntervalSeconds, status.WebcamIntervalSeconds, status.RequireWebcam);
        RequireReadiness = status.RequireReadiness;
        _requirements = new ReadinessRequirements(
            status.RequireWebcam,
            status.RequireMic,
            status.BlockMultiMonitor ?? AppConfigProvider.Current.BlockMultiMonitor,
            AppConfigProvider.ParseLockdownMode(status.LockdownMode ?? AppConfigProvider.Current.LockdownMode));
        if (!string.IsNullOrWhiteSpace(status.AllowedApps))
        {
            try
            {
                var apps = JsonSerializer.Deserialize<List<string>>(status.AllowedApps);
                if (apps is not null)
                {
                    foreach (var app in apps.Where(a => !string.IsNullOrWhiteSpace(a)))
                    {
                        AllowedApps.Add(app);
                        AllowedAppNames.Add(app);
                    }
                }
            }
            catch { /* malformed — leave empty */ }
        }
        if (AllowedApps.Count == 0) AllowedApps.Add("Only the exam app is allowed");
        OnPropertyChanged(nameof(HasAllowedApps));

        // Attendance is re-confirmed each refresh unless the exam is already running.
        // Readiness follows the server: a pass stays valid for 30 minutes.
        if (!ExamInProgress)
        {
            AttendanceConfirmed = false;
            AttendanceStatus = "Not checked in";
        }
        ReadinessPassed = status.ReadinessPassed;
        if (status.ReadinessPassed) ReadinessStatus = "Passed (valid 30 min)";
        else if (ReadinessItems.Count == 0) ReadinessStatus = status.RequireReadiness ? "Required before start" : "Optional for this exam";

        UpdateStartEligibility();

        _countdownTimer.Tick -= CountdownTick;
        _countdownTimer.Tick += CountdownTick;
        _countdownTimer.Start();
    }

    public bool HasAllowedApps => AllowedApps.Count > 0;

    private void CountdownTick(object? sender, EventArgs e)
    {
        if (_remainingSeconds <= 0)
        {
            RemainingTime = "00:00:00";
            CountdownLabel = "TIME REMAINING";
            LowTime = false;
            _countdownTimer.Stop();
            UpdateStartEligibility();
            return;
        }

        _remainingSeconds--;
        RefreshCountdownDisplay();

        // The opening moment passed while the student waited on the dashboard: unlock Start
        // (still subject to check-in/readiness) without re-polling, which would reset check-in.
        var open = _gate is not null && StartGate.HasOpened(_gate.OpensAtUtc, _clockOffset, DateTime.UtcNow);
        if (open && !_wasOpen)
        {
            _wasOpen = true;
            if (_gate!.IsEligible) Message = "The exam is open. Check in and start when you are ready.";
            UpdateStartEligibility();
        }
    }

    // Before the exam opens the big clock counts down to the opening; afterwards to the end.
    private void RefreshCountdownDisplay()
    {
        var untilOpen = _gate is null ? TimeSpan.Zero : StartGate.UntilOpen(_gate.OpensAtUtc, _clockOffset, DateTime.UtcNow);
        if (untilOpen > TimeSpan.Zero && _remainingSeconds > 0)
        {
            CountdownLabel = "STARTS IN";
            RemainingTime = StartGate.Format(untilOpen);
            LowTime = false;
            return;
        }

        CountdownLabel = "TIME REMAINING";
        RemainingTime = StartGate.Format(TimeSpan.FromSeconds(_remainingSeconds));
        LowTime = _remainingSeconds is > 0 and <= 300;
    }

    private void UpdateStartEligibility()
    {
        CanResumeExam = _gate is not null && StartGate.CanResume(_gate, _clockOffset, DateTime.UtcNow);
        var gateOpen = _gate is not null && StartGate.CanStart(_gate, _clockOffset, DateTime.UtcNow);
        CanStartExam = CanResumeExam || (gateOpen && AttendanceConfirmed && (!RequireReadiness || ReadinessPassed));
    }
}

/// <summary>One row of the readiness checklist.</summary>
public class ReadinessRow(string name) : BindableBase
{
    private bool _isRunning;
    private ReadinessItemResult? _result;

    public string Name { get; } = name;
    public string DisplayName { get; } = ReadinessPolicy.DisplayName(name);
    public ReadinessItemResult? Result => _result;

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (!SetProperty(ref _isRunning, value)) return;
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(Failed));
            OnPropertyChanged(nameof(Detail));
        }
    }

    public bool Passed => _result?.Passed == true;
    public bool Failed => _result is { Passed: false } && !_isRunning;
    public string Detail => _isRunning ? "Checking…" : _result?.Detail ?? "";
    /// "Running" | "Passed" | "Skipped" | "Failed" — drives the row icon.
    public string State => _isRunning ? "Running" : _result is null ? "Running" : _result.Skipped ? "Skipped" : _result.Passed ? "Passed" : "Failed";

    public void Apply(ReadinessItemResult result)
    {
        _result = result;
        _isRunning = false;
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(Passed));
        OnPropertyChanged(nameof(Failed));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(State));
    }
}