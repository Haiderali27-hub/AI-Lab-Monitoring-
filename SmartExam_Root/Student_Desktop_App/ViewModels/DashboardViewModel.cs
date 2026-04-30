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
    private bool _canStartExam;
    private int _remainingSeconds;

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

    public void UpdateMonitoringStatus(string status)
    {
        MonitoringStatus = status;
    }

    private void ApplyExamStatus(StudentExamStatus status)
    {
        ExamName = status.ExamName;
        ExamStatus = status.Status;
        Message = status.Message;
        _remainingSeconds = Math.Max(0, status.RemainingSeconds);
        RemainingTime = TimeSpan.FromSeconds(_remainingSeconds).ToString("hh\\:mm\\:ss");

        CanStartExam = status.IsEligible &&
                       _remainingSeconds > 0 &&
                       !string.Equals(status.Status, "InProgress", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(status.Status, "Submitted", StringComparison.OrdinalIgnoreCase);

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
}