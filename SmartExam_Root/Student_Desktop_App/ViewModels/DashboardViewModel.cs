using System.Windows.Threading;
using Student_Desktop_App.Core;
using Student_Desktop_App.Services;

namespace Student_Desktop_App.ViewModels;

public class DashboardViewModel(ApiClient apiClient) : BindableBase
{
    private readonly ApiClient _apiClient = apiClient;
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private string _studentDisplayName = "Student";
    private string _examName = "No active exam";
    private string _examStatus = "NotStarted";
    private string _remainingTime = "00:00:00";
    private string _monitoringStatus = "Monitoring inactive";
    private string _message = "Waiting for exam eligibility check...";
    private string _instructions = "";
    private string _labName = "";
    private string _proctorName = "";
    private string _attendanceStatus = "Not checked in";
    private string _identityStatus = "Not verified";
    private bool _attendanceConfirmed;
    private bool _identityVerified;
    private bool _canStartExam;
    private bool _baseEligibleToStart;
    private int _remainingSeconds;
    private Guid? _currentExamId;

    public Guid? ExamSessionId { get; private set; }

    public string StudentDisplayName
    {
        get => _studentDisplayName;
        set => SetProperty(ref _studentDisplayName, value);
    }

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

    public string IdentityStatus
    {
        get => _identityStatus;
        set => SetProperty(ref _identityStatus, value);
    }

    public bool AttendanceConfirmed
    {
        get => _attendanceConfirmed;
        set => SetProperty(ref _attendanceConfirmed, value);
    }

    public bool IdentityVerified
    {
        get => _identityVerified;
        set => SetProperty(ref _identityVerified, value);
    }

    public bool CanStartExam
    {
        get => _canStartExam;
        set => SetProperty(ref _canStartExam, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var token = SessionStore.CurrentToken;
        if (token is null)
        {
            Message = "Session not found. Please login again.";
            return;
        }

        StudentDisplayName = token.User.Username;

        var result = await _apiClient.GetCurrentExamStatusAsync(token.AccessToken, cancellationToken);
        if (!result.Success || result.Data is null)
        {
            Message = result.Message;
            CanStartExam = false;
            return;
        }

        ApplyExamStatus(result.Data);
    }

    public async Task<bool> StartExamAsync(CancellationToken cancellationToken = default)
    {
        var token = SessionStore.CurrentToken;
        if (token is null)
        {
            Message = "Session missing. Please login again.";
            return false;
        }

        if (!AttendanceConfirmed || !IdentityVerified)
        {
            Message = "Complete attendance and identity verification before starting the exam.";
            return false;
        }

        var result = await _apiClient.StartExamAsync(token.AccessToken, cancellationToken);
        if (!result.Success || result.Data is null)
        {
            Message = result.Message;
            return false;
        }

        ExamSessionId = result.Data.Id;
        ExamStatus = result.Data.Status;
        CanStartExam = false;
        Message = "Exam session started.";
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

    public async Task<bool> VerifyIdentityAsync(CancellationToken cancellationToken = default)
    {
        var token = SessionStore.CurrentToken;
        if (token is null || _currentExamId is null)
        {
            Message = "Exam session missing. Refresh and try again.";
            return false;
        }

        var payload = new { examId = _currentExamId, atUtc = DateTime.UtcNow };
        var result = await _apiClient.SendMonitoringEventAsync(token.AccessToken, "IdentityVerified", null, payload, cancellationToken);
        if (!result.Success)
        {
            Message = result.Message;
            return false;
        }

        IdentityVerified = true;
        IdentityStatus = "Verified";
        UpdateStartEligibility();
        return true;
    }

    public void UpdateMonitoringStatus(string status)
    {
        MonitoringStatus = status;
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
        _remainingSeconds = Math.Max(0, status.RemainingSeconds);
        RemainingTime = TimeSpan.FromSeconds(_remainingSeconds).ToString("hh\\:mm\\:ss");

        AttendanceConfirmed = false;
        IdentityVerified = false;
        AttendanceStatus = "Not checked in";
        IdentityStatus = "Not verified";

        _baseEligibleToStart = status.IsEligible &&
                       _remainingSeconds > 0 &&
                       !string.Equals(status.Status, "InProgress", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(status.Status, "Submitted", StringComparison.OrdinalIgnoreCase);

        UpdateStartEligibility();

        _countdownTimer.Tick -= CountdownTick;
        _countdownTimer.Tick += CountdownTick;
        _countdownTimer.Start();
    }

    private void CountdownTick(object? sender, EventArgs e)
    {
        if (_remainingSeconds <= 0)
        {
            RemainingTime = "00:00:00";
            _countdownTimer.Stop();
            return;
        }

        _remainingSeconds--;
        RemainingTime = TimeSpan.FromSeconds(_remainingSeconds).ToString("hh\\:mm\\:ss");
    }

    private void UpdateStartEligibility()
    {
        CanStartExam = _baseEligibleToStart && AttendanceConfirmed && IdentityVerified;
    }
}