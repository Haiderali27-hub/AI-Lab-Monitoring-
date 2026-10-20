using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Student_Desktop_App.Core;
using System.Windows.Threading;
using Student_Desktop_App.Core.Backup;
using Student_Desktop_App.Core.Lockdown;
using Student_Desktop_App.Services;
using Student_Desktop_App.Services.Backup;
using Student_Desktop_App.Services.Lockdown;
using Student_Desktop_App.ViewModels;

namespace Student_Desktop_App.Views;

public partial class ExamWindow : Window
{
    private readonly ExamSessionViewModel _viewModel;
    private readonly LockdownCoordinator _lockdown;
    // Module 6: an automatic submit that could not reach the server is retried until it can (D-19).
    private readonly DispatcherTimer _submitRetry = new() { Interval = TimeSpan.FromSeconds(10) };
    // Module 10: proctor warnings and manual unlock come through the monitoring service (heartbeat ack).
    private readonly LiveMonitoringService? _monitoring;
    private readonly DispatcherTimer _relockTimer = new();
    private DateTime? _unlockedUntil;   // this PC's UTC clock; null = locked
    private ExamQuestionItem? _current;
    private bool _submitted;
    private bool _submitting;
    private bool _suppressTextEvents;

    public bool Submitted => _submitted;

    /// <param name="monitoring">Module 5: the running monitoring service — lockdown violations go to its offline-safe
    /// queue and trigger an evidence capture. Null: violations are sent directly (no capture).</param>
    public ExamWindow(ApiClient apiClient, string accessToken, Guid examId, Guid sessionId, Guid userId, int remainingSeconds,
        IEnumerable<string> allowedApps, string? examLockdownMode = null, bool? examBlockMultiMonitor = null,
        LiveMonitoringService? monitoring = null)
    {
        InitializeComponent();
        _monitoring = monitoring;
        // The exam's server-side settings win; the local config only applies when the server sends none.
        var config = AppConfigProvider.Current;
        _lockdown = new LockdownCoordinator(apiClient, accessToken, sessionId,
            AppConfigProvider.ParseLockdownMode(examLockdownMode ?? config.LockdownMode), allowedApps,
            examBlockMultiMonitor ?? config.BlockMultiMonitor, config.DisableTaskManager,
            monitoring is null ? null : monitoring.ReportViolation);
        _viewModel = new ExamSessionViewModel(apiClient, accessToken, sessionId, examId, userId, remainingSeconds, OpenBackupStore());
        _viewModel.TimeExpired += () => Dispatcher.Invoke(async () => await AutoSubmitOnExpiry());
        // An edited local backup is ignored (server copy wins) and reported to the proctors.
        _viewModel.BackupTampered += reason =>
        {
            var payload = new { reason, detail = "Local answer backup failed its integrity check", atUtc = DateTime.UtcNow };
            if (monitoring is not null) monitoring.ReportEvent("BackupTampered", payload, urgent: true);
            else _ = apiClient.SendMonitoringEventAsync(accessToken, "BackupTampered", sessionId, payload);
        };
        _submitRetry.Tick += async (_, _) =>
        {
            _submitRetry.Stop();
            await DoSubmit(auto: true);
        };
        // The unlock ends on this PC's own clock too, so a network outage during the unlock never leaves it open.
        _relockTimer.Tick += (_, _) => ApplyUnlockDeadline(null);
        // Save the answer being edited whenever the window loses focus (a crash right after still loses nothing).
        Deactivated += async (_, _) =>
        {
            if (_current is not null && !_submitted) await _viewModel.SaveAnswerAsync(_current);
        };
        DataContext = _viewModel;
    }

    // DPAPI-protected local backup; without one (profile problem) the exam still runs on server saves alone.
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

    /// <summary>Server clock from the heartbeat ack: exam extensions and this student's extra time (D-16).</summary>
    public void ResyncRemaining(int seconds) => _viewModel.ResyncRemaining(seconds);

    /// <summary>
    /// The heartbeat reports the session's server status. Anything but InProgress means a proctor force-submitted
    /// it or the server timed it out: the answers are locked, so release the student instead of leaving them in
    /// lockdown until the timer runs out (D-17).
    /// </summary>
    public void OnServerSessionStatus(string status)
    {
        if (_submitted || _submitting || string.Equals(status, "InProgress", StringComparison.OrdinalIgnoreCase)) return;

        _submitted = true;
        _submitRetry.Stop();
        _viewModel.MarkFinalisedByServer();
        MessageBox.Show(this, $"Your exam was {Describe(status)}. Answers saved before that point are kept.",
            "Exam Ended", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    /// <summary>
    /// Module 10 (D-05): shows the oldest unacknowledged proctor warning in the banner at the top of the exam window
    /// (non-modal: the student keeps working). Called on the UI thread when the heartbeat ack brings new warnings.
    /// </summary>
    public void ShowProctorWarnings()
    {
        var current = _monitoring?.Warnings.Current;
        if (current is null || _submitted)
        {
            WarningBanner.Visibility = Visibility.Collapsed;
            return;
        }

        var more = _monitoring!.Warnings.Pending.Count - 1;
        WarningTitleText.Text = $"{current.Title} · {current.CreatedAt.ToLocalTime():HH:mm}";
        WarningBodyText.Text = current.Body;
        WarningMoreText.Text = more > 0 ? $"{more} more warning(s) after this one" : string.Empty;
        WarningMoreText.Visibility = more > 0 ? Visibility.Visible : Visibility.Collapsed;
        WarningBanner.Visibility = Visibility.Visible;
        // Bring the exam to the front, except while a proctor has unlocked the PC and is using another app.
        if (!IsActive && _unlockedUntil is null) Activate();
    }

    private async void AcknowledgeWarningButton_Click(object sender, RoutedEventArgs e)
    {
        if (_monitoring?.Warnings.Current is not { } current) return;
        AcknowledgeWarningButton.IsEnabled = false;
        try
        {
            await _monitoring.AcknowledgeWarningAsync(current.NotificationId);
        }
        finally
        {
            AcknowledgeWarningButton.IsEnabled = true;
            ShowProctorWarnings();
        }
    }

    /// <summary>
    /// Module 10 manual unlock: the latest unlock deadline from the heartbeat ack (this PC's clock; null = locked).
    /// Suspends the lockdown guards while a proctor works on the PC, lets the window go behind other apps, and turns
    /// everything back on when the deadline passes or the proctor relocks. UI thread.
    /// </summary>
    public void ApplyUnlockDeadline(DateTime? localDeadline)
    {
        var now = DateTime.UtcNow;
        switch (LockdownSuspendPolicy.Decide(_unlockedUntil, localDeadline, now))
        {
            case SuspendAction.Suspend:
                _unlockedUntil = localDeadline;
                _lockdown.Suspend();
                Topmost = false;
                _monitoring?.ReportEvent("LockdownSuspended", new { untilUtc = localDeadline, atUtc = now });
                ScheduleRelock();
                break;

            case SuspendAction.Reschedule:
                _unlockedUntil = localDeadline;
                ScheduleRelock();
                break;

            case SuspendAction.Resume:
                _unlockedUntil = null;
                _relockTimer.Stop();
                _lockdown.Resume();
                Topmost = true;
                if (!_submitted) Activate();
                _monitoring?.ReportEvent("LockdownResumed", new { atUtc = now });
                break;
        }

        UnlockBanner.Visibility = _unlockedUntil is null ? Visibility.Collapsed : Visibility.Visible;
        if (_unlockedUntil is DateTime until)
            UnlockText.Text = $"A proctor unlocked this PC until {until.ToLocalTime():HH:mm}. Exam lockdown is paused and resumes automatically; monitoring continues.";
    }

    private void ScheduleRelock()
    {
        _relockTimer.Stop();
        if (_unlockedUntil is not DateTime until) return;
        var wait = until - DateTime.UtcNow;
        _relockTimer.Interval = wait > TimeSpan.Zero ? wait : TimeSpan.FromMilliseconds(1);
        _relockTimer.Start();
    }

    private static string Describe(string? status) => status switch
    {
        "ForceSubmitted" => "force-submitted by the proctor",
        "TimedOut" => "closed by the server at the deadline",
        "Submitted" => "submitted",
        _ => "closed"
    };

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // The exam window is pinned fullscreen while locked down.
        if (!_lockdown.IsActive && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _lockdown.Start(this);
        // A warning that arrived before this window opened (e.g. while resuming) is shown now.
        ShowProctorWarnings();
        // The window is locked down, so never leave the student staring at an empty exam: retry until loaded.
        // Stops once the window was closed meanwhile (the server force-submitted or timed out the session).
        while (!await _viewModel.LoadAsync())
        {
            if (_submitted) return;
            _viewModel.SaveStatus = "Could not load the questions — retrying…";
            await Task.Delay(TimeSpan.FromSeconds(5));
            if (_submitted) return;
        }
        if (_viewModel.Questions.Count > 0)
        {
            ShowQuestion(_viewModel.Questions[0]);
        }
    }

    // Render a question into the answer panel and animate the card in.
    private async void ShowQuestion(ExamQuestionItem q)
    {
        // persist the answer of the question we're leaving
        if (_current is not null) await _viewModel.SaveAnswerAsync(_current);

        _current = q;
        QTypeLabel.Text = q.TypeLabel;
        QMarks.Text = $"{q.Marks} mark{(q.Marks == 1 ? "" : "s")}";
        QBody.Text = $"Q{q.Number}.  {q.BodyText}";

        if (q.IsMcq)
        {
            OptionsList.ItemsSource = q.Options;
            OptionsList.Visibility = Visibility.Visible;
            WrittenArea.Visibility = Visibility.Collapsed;
        }
        else
        {
            OptionsList.Visibility = Visibility.Collapsed;
            WrittenArea.Visibility = Visibility.Visible;
            WrittenLabel.Text = q.IsCoding ? "YOUR CODE" : "YOUR ANSWER";
            _suppressTextEvents = true;
            AnswerBox.Text = q.AnswerText;
            AnswerBox.FontFamily = q.IsCoding ? new System.Windows.Media.FontFamily("Consolas") : new System.Windows.Media.FontFamily("Segoe UI");
            _suppressTextEvents = false;
            AnswerBox.Focus();
        }

        UpdateNavButtons();

        if (TryFindResource("RiseIn") is Storyboard sb)
            sb.Begin(QuestionCard);
    }

    private void UpdateNavButtons()
    {
        var idx = _current is null ? -1 : _viewModel.Questions.IndexOf(_current);
        PrevButton.IsEnabled = idx > 0;
        NextButton.IsEnabled = idx >= 0 && idx < _viewModel.Questions.Count - 1;
    }

    private void OptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || sender is not Button { Tag: ExamOptionItem opt }) return;
        _current.SelectOption(opt.Index);
        _viewModel.OnAnswerEdited(_current);
    }

    private void AnswerBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextEvents || _current is null) return;
        _current.AnswerText = AnswerBox.Text;
        _viewModel.OnAnswerEdited(_current);
    }

    private async void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ExamQuestionItem q } && q != _current)
        {
            await Task.Yield();
            ShowQuestion(q);
        }
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var idx = _viewModel.Questions.IndexOf(_current);
        if (idx > 0) ShowQuestion(_viewModel.Questions[idx - 1]);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var idx = _viewModel.Questions.IndexOf(_current);
        if (idx < _viewModel.Questions.Count - 1) ShowQuestion(_viewModel.Questions[idx + 1]);
    }

    private async void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        var unanswered = _viewModel.Questions.Count(q => !q.IsAnswered);
        var prompt = unanswered > 0
            ? $"You have {unanswered} unanswered question(s). Submit anyway? You cannot change answers after submitting."
            : "Submit your exam? You cannot change answers after submitting.";
        if (MessageBox.Show(this, prompt, "Submit Exam", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await DoSubmit(auto: false);
    }

    private async Task AutoSubmitOnExpiry()
    {
        MessageBox.Show(this, "Time is up. Your exam is being submitted automatically.", "Time Expired", MessageBoxButton.OK, MessageBoxImage.Information);
        await DoSubmit(auto: true);
    }

    private async Task DoSubmit(bool auto)
    {
        if (_submitted || _submitting) return;
        _submitting = true;
        SubmitButton.IsEnabled = false;
        try
        {
            if (_current is not null) await _viewModel.SaveAnswerAsync(_current);

            var (result, receipt) = await _viewModel.SubmitAsync();
            switch (result.Status)
            {
                case SubmitStatus.Submitted:
                    _submitted = true;
                    _submitRetry.Stop();
                    ShowReceipt(receipt);
                    Close();
                    return;

                case SubmitStatus.AlreadyFinalised:
                    _submitted = true;
                    _submitRetry.Stop();
                    MessageBox.Show(this, $"Your exam was already {Describe(result.FinalStatus)}. Answers saved before that point are kept.",
                        "Exam Ended", MessageBoxButton.OK, MessageBoxImage.Information);
                    Close();
                    return;

                case SubmitStatus.Offline:
                    // Never close while answers may exist only on this PC (D-19): keep them, keep trying.
                    _viewModel.SaveStatus = result.Message;
                    if (auto) _submitRetry.Start();
                    else MessageBox.Show(this,
                        "The exam server cannot be reached. Your answers are saved on this PC and will be sent when the network is back. Press Submit again then.",
                        "Waiting for Network", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;

                default:
                    if (auto) _submitRetry.Start();
                    else MessageBox.Show(this, $"Could not submit: {result.Message}", "Submit Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
            }
        }
        finally
        {
            _submitting = false;
            if (!_submitted) SubmitButton.IsEnabled = true;
        }
    }

    // Module 6 receipt dialog (M6-D-04): the server's hash, and whether it matches the answers on this PC.
    private void ShowReceipt(ReceiptView? receipt)
    {
        if (receipt is null)
        {
            MessageBox.Show(this, "Your exam has been submitted successfully.", "Submitted", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var check = receipt.Verified
            ? "Verified: the server stored exactly the answers on this PC."
            : "Mismatch: the server copy differs from this PC. Tell the invigilator now and keep this receipt.";
        MessageBox.Show(this,
            "Your exam has been submitted." + Environment.NewLine + Environment.NewLine +
            $"Submission receipt (SHA-256 of {receipt.AnswerCount} answer(s)):{Environment.NewLine}{receipt.ServerSha256}" +
            Environment.NewLine + Environment.NewLine +
            $"Submitted at {receipt.SubmittedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}" + Environment.NewLine + Environment.NewLine + check,
            receipt.Verified ? "Submitted: Receipt" : "Submitted: Receipt Mismatch",
            MessageBoxButton.OK, receipt.Verified ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // Alt+F4 is swallowed by the hook; this also refuses any other close attempt until the
        // exam is submitted (manually, on time expiry, or by the proctor via force-submit).
        if (_lockdown.IsActive && !_submitted)
        {
            e.Cancel = true;
            return;
        }

        _submitRetry.Stop();
        _relockTimer.Stop();
        _lockdown.Dispose();
        _viewModel.Stop();
    }
}
