using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Student_Desktop_App.Core.Backup;
using Student_Desktop_App.Services;
using Student_Desktop_App.Services.Backup;
using Student_Desktop_App.ViewModels;

namespace Student_Desktop_App.Views;

public partial class ExamDashboard : Window
{
    private readonly ApiClient _apiClient;
    private readonly DashboardViewModel _viewModel;
    private readonly LiveMonitoringService _monitoringService;
    // The exam window while it is open: heartbeat clock resyncs and server status changes go to it too (D-16, D-17).
    private ExamWindow? _examWindow;

    // Watches the session while the student sits on the dashboard (before the exam's
    // heartbeat loop starts): polls session validity + proctor messages every 15s so a
    // force-logout / deactivation kicks them out even pre-exam.
    private readonly DispatcherTimer _sessionWatch = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _kicked;

    public ExamDashboard(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _viewModel = new DashboardViewModel(_apiClient, OpenBackupStore());
        _monitoringService = new LiveMonitoringService(_apiClient);
        _monitoringService.StatusChanged += status => Dispatcher.Invoke(() => _viewModel.UpdateMonitoringStatus(status));
        // Resync the countdowns from the server's heartbeat ack (teacher extensions, per-student extra time)
        _monitoringService.RemainingSecondsUpdated += seconds => Dispatcher.Invoke(() =>
        {
            _viewModel.UpdateRemainingSeconds(seconds);
            _examWindow?.ResyncRemaining(seconds);
        });
        // Proctor force-submit / server timeout: release the student from the locked exam window (D-17)
        _monitoringService.SessionStatusUpdated += status => Dispatcher.Invoke(() => _examWindow?.OnServerSessionStatus(status));
        // Module 10: proctor warnings (D-05) and manual unlock, both from the heartbeat ack
        _monitoringService.WarningsReceived += _ => Dispatcher.Invoke(() => _examWindow?.ShowProctorWarnings());
        _monitoringService.UnlockDeadlineReceived += until => Dispatcher.Invoke(() => _examWindow?.ApplyUnlockDeadline(until));
        // Server revoked the session mid-exam (force-logout / deactivation)
        _monitoringService.SessionRevoked += () => Dispatcher.Invoke(() => KickToLogin("Your session was ended by an administrator."));

        _sessionWatch.Tick += SessionWatch_Tick;
        // The start gate can open on its own (countdown reaches the exam's opening time).
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DashboardViewModel.CanStartExam))
                StartExamButton.IsEnabled = _viewModel.CanStartExam;
        };
        DataContext = _viewModel;
    }

    private static AnswerBackupStore? OpenBackupStore()
    {
        try
        {
            return DpapiKeyProtector.CreateDefaultStore();
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Shows the exam window modally; while it is open, heartbeat updates are forwarded to it.
    private void RunExamWindow(ExamWindow examWindow)
    {
        _examWindow = examWindow;
        try
        {
            Hide();
            examWindow.ShowDialog();
        }
        finally
        {
            _examWindow = null;
            Show();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        StartExamButton.IsEnabled = _viewModel.CanStartExam;
        _sessionWatch.Start();
    }

    // Pre-exam session/notification poll. If the token is no longer valid, kick out.
    private async void SessionWatch_Tick(object? sender, EventArgs e)
    {
        var token = SessionStore.CurrentToken;
        if (token is null) return;

        var alive = await _apiClient.IsSessionAliveAsync(token.AccessToken);
        if (!alive)
        {
            KickToLogin("Your session was ended by an administrator.");
            return;
        }

        await _viewModel.RefreshNotificationsAsync();
    }

    // Cleanly tears down and returns to the login window with a message.
    private void KickToLogin(string reason)
    {
        if (_kicked) return;
        _kicked = true;

        _sessionWatch.Stop();
        _monitoringService.Stop();
        SessionStore.Clear();

        MessageBox.Show(reason, "Session Ended", MessageBoxButton.OK, MessageBoxImage.Warning);

        var loginWindow = new LoginWindow();
        loginWindow.Show();
        Close();
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        StartExamButton.IsEnabled = _viewModel.CanStartExam;
        CheckInButton.IsEnabled = true;
        RunReadinessButton.IsEnabled = true;
    }

    private async void CheckInButton_OnClick(object sender, RoutedEventArgs e)
    {
        CheckInButton.IsEnabled = false;
        var ok = await _viewModel.ConfirmAttendanceAsync();
        if (!ok)
        {
            CheckInButton.IsEnabled = true; // let the student retry on failure
        }
        StartExamButton.IsEnabled = _viewModel.CanStartExam;
    }

    private async void RunReadinessButton_OnClick(object sender, RoutedEventArgs e)
    {
        RunReadinessButton.IsEnabled = false;
        StartExamButton.IsEnabled = false;
        try
        {
            await _viewModel.RunReadinessAsync();
        }
        finally
        {
            // A pass is valid for 30 min, so allow re-running at any time.
            RunReadinessButton.IsEnabled = true;
            StartExamButton.IsEnabled = _viewModel.CanStartExam;
        }
    }

    private async void RetryReadinessItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string check }) return;
        StartExamButton.IsEnabled = false;
        await _viewModel.RetryReadinessItemAsync(check);
        StartExamButton.IsEnabled = _viewModel.CanStartExam;
    }

    // Practice (mock) exam: same exam window and monitoring as the real thing, but ungraded
    // and repeatable. Lockdown runs in Report mode so a rehearsal never kills the student's apps.
    private async void PracticeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (PracticeExamPicker.SelectedItem is not PracticeExamSummary practice) return;
        var token = SessionStore.CurrentToken;
        if (token is null) return;

        PracticeButton.IsEnabled = false;
        try
        {
            var (sessionId, message) = await _apiClient.StartSessionAsync(token.AccessToken, practice.ExamId);
            if (sessionId is not Guid sid)
            {
                MessageBox.Show(this, message, "Practice exam", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Practice exams never use the webcam; the screen capture interval follows the heartbeat ack.
            _monitoringService.Start(token.AccessToken, sid, Array.Empty<string>().ToList(), new CaptureSettings());
            var examWindow = new ExamWindow(_apiClient, token.AccessToken, practice.ExamId, sid, token.User.Id, practice.DurationMinutes * 60,
                new List<string>(), "Report", false, _monitoringService)
            {
                Owner = this
            };
            RunExamWindow(examWindow);
            _monitoringService.Stop();
            await _viewModel.InitializeAsync();
            StartExamButton.IsEnabled = _viewModel.CanStartExam;
        }
        finally
        {
            PracticeButton.IsEnabled = true;
        }
    }

    private async void StartExamButton_OnClick(object sender, RoutedEventArgs e)
    {
        StartExamButton.IsEnabled = false;
        var started = await _viewModel.StartExamAsync();
        if (!started)
        {
            StartExamButton.IsEnabled = _viewModel.CanStartExam;
            return;
        }

        var token = SessionStore.CurrentToken;
        if (token is not null)
        {
            _monitoringService.Start(token.AccessToken, _viewModel.ExamSessionId, _viewModel.AllowedAppNames, _viewModel.ExamCapture);

            // Open the exam-taking window with the real questions.
            if (_viewModel.CurrentExamId is Guid examId && _viewModel.ExamSessionId is Guid sessionId)
            {
                var examWindow = new ExamWindow(_apiClient, token.AccessToken, examId, sessionId, token.User.Id, _viewModel.RemainingSecondsSnapshot,
                    _viewModel.AllowedAppNames, _viewModel.ExamLockdownMode, _viewModel.ExamBlockMultiMonitor, _monitoringService)
                {
                    Owner = this
                };
                RunExamWindow(examWindow);

                // Exam finished (submitted or window closed) — stop monitoring and refresh.
                _monitoringService.Stop();
                await _viewModel.InitializeAsync();
                StartExamButton.IsEnabled = _viewModel.CanStartExam;
            }
        }
    }

    private async void LogoutButton_OnClick(object sender, RoutedEventArgs e)
    {
        _sessionWatch.Stop();
        _monitoringService.Stop();

        var token = SessionStore.CurrentToken;
        if (token is not null)
        {
            await _apiClient.LogoutAsync(token.AccessToken);
        }

        SessionStore.Clear();

        var loginWindow = new LoginWindow();
        loginWindow.Show();
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _sessionWatch.Stop();
        _monitoringService.Stop();
    }
}
