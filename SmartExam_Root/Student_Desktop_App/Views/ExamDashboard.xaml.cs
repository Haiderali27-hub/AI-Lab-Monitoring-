using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Student_Desktop_App.Services;
using Student_Desktop_App.ViewModels;

namespace Student_Desktop_App.Views;

public partial class ExamDashboard : Window
{
    private readonly ApiClient _apiClient;
    private readonly DashboardViewModel _viewModel;
    private readonly LiveMonitoringService _monitoringService;

    // Watches the session while the student sits on the dashboard (before the exam's
    // heartbeat loop starts): polls session validity + proctor messages every 15s so a
    // force-logout / deactivation kicks them out even pre-exam.
    private readonly DispatcherTimer _sessionWatch = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _kicked;

    public ExamDashboard(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _viewModel = new DashboardViewModel(_apiClient);
        _monitoringService = new LiveMonitoringService(_apiClient);
        _monitoringService.StatusChanged += status => Dispatcher.Invoke(() => _viewModel.UpdateMonitoringStatus(status));
        // Resync the countdown from the server's heartbeat ack (covers teacher time extensions)
        _monitoringService.RemainingSecondsUpdated += seconds => Dispatcher.Invoke(() => _viewModel.UpdateRemainingSeconds(seconds));
        // Server revoked the session mid-exam (force-logout / deactivation)
        _monitoringService.SessionRevoked += () => Dispatcher.Invoke(() => KickToLogin("Your session was ended by an administrator."));

        _sessionWatch.Tick += SessionWatch_Tick;
        DataContext = _viewModel;
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
        VerifyIdentityButton.IsEnabled = true;
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

    private async void VerifyIdentityButton_OnClick(object sender, RoutedEventArgs e)
    {
        VerifyIdentityButton.IsEnabled = false;
        var ok = await _viewModel.VerifyIdentityAsync();
        if (!ok)
        {
            VerifyIdentityButton.IsEnabled = true;
        }
        StartExamButton.IsEnabled = _viewModel.CanStartExam;
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
            _monitoringService.Start(token.AccessToken, _viewModel.ExamSessionId);

            // Open the exam-taking window with the real questions.
            if (_viewModel.CurrentExamId is Guid examId && _viewModel.ExamSessionId is Guid sessionId)
            {
                var examWindow = new ExamWindow(_apiClient, token.AccessToken, examId, sessionId, _viewModel.RemainingSecondsSnapshot)
                {
                    Owner = this
                };
                Hide();
                examWindow.ShowDialog();
                Show();

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
