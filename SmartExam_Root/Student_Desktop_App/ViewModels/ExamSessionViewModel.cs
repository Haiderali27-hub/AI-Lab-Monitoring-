using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Threading;
using Student_Desktop_App.Core;
using Student_Desktop_App.Core.Backup;
using Student_Desktop_App.Core.Submission;
using Student_Desktop_App.Services;

namespace Student_Desktop_App.ViewModels;

// One selectable option on an MCQ.
public class ExamOptionItem : BindableBase
{
    private bool _isSelected;
    public int Index { get; init; }
    public string Letter => ((char)('A' + Index)).ToString();
    public string Text { get; init; } = string.Empty;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

// A single exam question the student answers.
public class ExamQuestionItem : BindableBase
{
    private string _answerText = string.Empty;
    private int _selectedOptionIndex = -1;

    public Guid QuestionId { get; init; }
    public string Type { get; init; } = "Theory";
    public int Number { get; init; }
    public int Marks { get; init; }
    public string BodyText { get; init; } = string.Empty;

    public bool IsMcq => Type.Equals("Mcq", StringComparison.OrdinalIgnoreCase);
    public bool IsCoding => Type.Equals("Coding", StringComparison.OrdinalIgnoreCase);
    public bool IsWritten => !IsMcq;
    public string TypeLabel => IsMcq ? "MULTIPLE CHOICE" : IsCoding ? "CODING" : "THEORY";

    public ObservableCollection<ExamOptionItem> Options { get; } = new();

    public string AnswerText { get => _answerText; set => SetProperty(ref _answerText, value); }
    public int SelectedOptionIndex { get => _selectedOptionIndex; set => SetProperty(ref _selectedOptionIndex, value); }

    // Module 6 sync state. Revision counts local edits (sent with every save); AckedRevision is the highest
    // revision the server confirmed; OnServer = the server holds a row for this answer (it is in the receipt).
    public int Revision { get; set; }
    public int AckedRevision { get; set; }
    public bool OnServer { get; set; }
    public bool HasUnsentEdits => Revision > AckedRevision;

    // The value sent to the server: MCQ → selected option index; written → the text.
    public string ServerAnswer => IsMcq
        ? (SelectedOptionIndex >= 0 ? SelectedOptionIndex.ToString() : string.Empty)
        : AnswerText ?? string.Empty;

    public bool IsAnswered => IsMcq ? SelectedOptionIndex >= 0 : !string.IsNullOrWhiteSpace(AnswerText);

    public void SelectOption(int index)
    {
        SelectedOptionIndex = index;
        foreach (var o in Options) o.IsSelected = o.Index == index;
    }

    /// <summary>Restores a saved value (server copy or local backup) without counting it as an edit.</summary>
    public void Restore(string serverAnswer)
    {
        if (IsMcq) SelectOption(int.TryParse(serverAnswer?.Trim(), out var i) && i >= 0 && i < Options.Count ? i : -1);
        else AnswerText = serverAnswer ?? string.Empty;
    }
}

/// <summary>The receipt as the exam window shows it: the server's hash, the locally computed one, and whether they agree.</summary>
public record ReceiptView(DateTime SubmittedAtUtc, int AnswerCount, string ServerSha256, string LocalSha256, bool Verified);

/// <summary>
/// The exam-taking session. Module 6: every edit bumps the answer's revision and marks it dirty; a 1 s tick
/// auto-saves answers 10 s after their first unsaved edit (<see cref="AutosaveScheduler"/>), replays saves that
/// failed for lack of network in order (<see cref="OfflineSaveQueue"/>) and keeps an AES-GCM encrypted copy of
/// every answer on disk (<see cref="AnswerBackupStore"/>), so a crash, power cut or dropped LAN loses nothing.
/// On load the server's answers are merged with that backup (<see cref="AnswerMerge"/>), which is how a
/// relaunched app resumes. Submit flushes everything first and refuses to report success while answers are
/// still only on this PC.
/// </summary>
public class ExamSessionViewModel : BindableBase
{
    public const string OfflineStatus = "Offline — saved on this PC, will sync";
    private static readonly TimeSpan OfflineRetryDelay = TimeSpan.FromSeconds(5);

    private readonly ApiClient _apiClient;
    private readonly string _accessToken;
    private readonly Guid _sessionId;
    private readonly Guid _examId;
    private readonly Guid _userId;
    private readonly AnswerBackupStore? _backup;
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly AutosaveScheduler _autosave = new(() => DateTime.UtcNow);
    private readonly OfflineSaveQueue _offline = new();
    private DateTime _nextOfflineRetryUtc = DateTime.MinValue;
    private bool _backupDirty;
    private bool _syncing;
    private bool _submitting;
    private bool _stopped;
    private int _remainingSeconds;

    private string _examTitle = "Exam";
    private string _remainingTime = "00:00:00";
    private bool _lowTime;
    private string _saveStatus = "";
    private int _answeredCount;
    private bool _isOffline;

    public ExamSessionViewModel(ApiClient apiClient, string accessToken, Guid sessionId, Guid examId, Guid userId,
        int remainingSeconds, AnswerBackupStore? backup)
    {
        _apiClient = apiClient;
        _accessToken = accessToken;
        _sessionId = sessionId;
        _examId = examId;
        _userId = userId;
        _backup = backup;
        _remainingSeconds = Math.Max(0, remainingSeconds);
        RemainingTime = TimeSpan.FromSeconds(_remainingSeconds).ToString("hh\\:mm\\:ss");
    }

    public ObservableCollection<ExamQuestionItem> Questions { get; } = new();
    public event Action? TimeExpired;
    /// <summary>The local backup failed its integrity check (reported to the proctor as BackupTampered).</summary>
    public event Action<string>? BackupTampered;

    public string ExamTitle { get => _examTitle; set => SetProperty(ref _examTitle, value); }
    public string RemainingTime { get => _remainingTime; set => SetProperty(ref _remainingTime, value); }
    public bool LowTime { get => _lowTime; set => SetProperty(ref _lowTime, value); }
    public string SaveStatus { get => _saveStatus; set => SetProperty(ref _saveStatus, value); }
    public bool IsOffline { get => _isOffline; private set => SetProperty(ref _isOffline, value); }

    public int AnsweredCount { get => _answeredCount; set { if (SetProperty(ref _answeredCount, value)) OnPropertyChanged(nameof(ProgressLabel)); } }
    public string ProgressLabel => $"{AnsweredCount} of {Questions.Count} answered";

    /// <summary>Answers not yet confirmed by the server (queued offline or edited since the last save).</summary>
    public int UnsentCount => Questions.Count(q => q.HasUnsentEdits);

    public async Task<bool> LoadAsync()
    {
        var detail = await _apiClient.GetExamDetailAsync(_accessToken, _examId);
        if (detail is null) return false;

        ExamTitle = detail.Title;
        var ordered = detail.Questions.OrderBy(q => q.OrderIndex).ToList();
        int n = 1;
        foreach (var q in ordered)
        {
            var item = new ExamQuestionItem
            {
                QuestionId = q.QuestionId,
                Type = q.Type,
                Number = n++,
                Marks = q.Marks,
                BodyText = q.BodyText
            };
            if (item.IsMcq && !string.IsNullOrWhiteSpace(q.OptionsJson))
            {
                try
                {
                    var opts = JsonSerializer.Deserialize<List<string>>(q.OptionsJson) ?? new();
                    for (int i = 0; i < opts.Count; i++)
                        item.Options.Add(new ExamOptionItem { Index = i, Text = opts[i] });
                }
                catch { /* malformed options — leave empty */ }
            }
            Questions.Add(item);
        }
        OnPropertyChanged(nameof(ProgressLabel));

        await RestoreAnswersAsync();

        _countdownTimer.Tick += CountdownTick;
        _countdownTimer.Start();
        return true;
    }

    // Crash recovery: server answers + encrypted local backup, higher revision wins; unconfirmed ones are replayed.
    private async Task RestoreAnswersAsync()
    {
        var server = await _apiClient.GetSessionAnswersAsync(_accessToken, _sessionId);
        if (server is not null && server.RemainingSeconds > 0) _remainingSeconds = server.RemainingSeconds;

        IReadOnlyList<BackupAnswer>? local = null;
        if (_backup is not null)
        {
            var loaded = _backup.Load(_sessionId);
            if (loaded.Status == BackupLoadStatus.Tampered)
            {
                SaveStatus = "Local backup was altered — it was ignored; the server copy is used.";
                BackupTampered?.Invoke(loaded.Error ?? "Integrity check failed.");
            }
            else if (loaded.State is { } state && state.UserId == _userId && state.ExamId == _examId)
            {
                local = state.Answers;
            }
        }

        var merged = AnswerMerge.Merge(
            server?.Answers.Select(a => new ServerAnswer(a.QuestionId, a.AnswerText ?? string.Empty, a.Revision)), local);
        var byId = Questions.ToDictionary(q => q.QuestionId);
        var replayed = 0;
        foreach (var m in merged)
        {
            if (!byId.TryGetValue(m.QuestionId, out var q)) continue;
            q.Restore(m.Text);
            q.Revision = m.Revision;
            q.AckedRevision = m.NeedsSync ? m.AckedRevision : m.Revision;
            q.OnServer = m.OnServer;
            if (m.NeedsSync)
            {
                _offline.Enqueue(new PendingSave(q.QuestionId, q.ServerAnswer, q.Revision, DateTime.UtcNow));
                replayed++;
            }
        }

        RecomputeAnswered();
        if (replayed > 0)
        {
            SaveStatus = $"Restored {replayed} answer(s) saved on this PC — syncing…";
            await FlushOfflineAsync();
        }
        _backupDirty = merged.Count > 0;
        WriteBackupIfDirty();
    }

    public void RecomputeAnswered()
    {
        AnsweredCount = Questions.Count(q => q.IsAnswered);
    }

    /// <summary>The student changed an answer (text typed, option picked): new revision, autosave in 10 s, backup in ≤ 1 s.</summary>
    public void OnAnswerEdited(ExamQuestionItem q)
    {
        q.Revision++;
        _autosave.MarkDirty(q.QuestionId);
        _backupDirty = true;
        RecomputeAnswered();
    }

    /// <summary>Saves one answer now (question change, window lost focus). Offline → queued, never lost.</summary>
    public async Task SaveAnswerAsync(ExamQuestionItem q)
    {
        _autosave.MarkClean(q.QuestionId);
        await SendAsync(q);
        WriteBackupIfDirty();
    }

    private async Task SendAsync(ExamQuestionItem q)
    {
        if (!q.HasUnsentEdits) return;
        var save = new PendingSave(q.QuestionId, q.ServerAnswer, q.Revision, DateTime.UtcNow);

        // A backlog is replayed first so the server sees edits in the order they were made.
        if (_offline.Count > 0)
        {
            _offline.Enqueue(save);
            await FlushOfflineAsync();
            return;
        }

        SaveStatus = "Saving…";
        if (await SendPendingAsync(save) == SaveOutcome.Offline) _offline.Enqueue(save);
    }

    private async Task<SaveOutcome> SendPendingAsync(PendingSave save)
    {
        var (outcome, message) = await _apiClient.SaveAnswerAsync(_accessToken, _sessionId, save);
        var q = Questions.FirstOrDefault(x => x.QuestionId == save.QuestionId);
        switch (outcome)
        {
            case SaveOutcome.Accepted:
            case SaveOutcome.Stale:
                if (q is not null)
                {
                    q.AckedRevision = Math.Max(q.AckedRevision, save.Revision);
                    q.OnServer = true;
                }
                IsOffline = false;
                _backupDirty = true;
                SaveStatus = UnsentCount > 0 || _offline.Count > 1 ? "Syncing…" : $"Saved {DateTime.Now:T}";
                break;
            case SaveOutcome.Offline:
                IsOffline = true;
                SaveStatus = OfflineStatus;
                break;
            case SaveOutcome.Rejected:
                // e.g. "Exam time is over" — the answer stays on this PC (backup) but cannot reach the server.
                SaveStatus = $"Not saved: {message}";
                break;
        }
        return outcome;
    }

    private async Task<bool> FlushOfflineAsync()
    {
        if (_offline.Count == 0) return true;
        var result = await _offline.FlushAsync(SendPendingAsync);
        _nextOfflineRetryUtc = result.Remaining > 0 ? DateTime.UtcNow + OfflineRetryDelay : DateTime.MinValue;
        if (result.Remaining == 0 && !IsOffline && result.Delivered > 0) SaveStatus = $"Synced {DateTime.Now:T}";
        return result.Remaining == 0;
    }

    // Runs once a second (from the countdown tick): due autosaves, offline replay, backup write.
    private async Task SyncTickAsync()
    {
        if (_syncing || _submitting || _stopped) return;
        _syncing = true;
        try
        {
            foreach (var id in _autosave.TakeDue())
            {
                var q = Questions.FirstOrDefault(x => x.QuestionId == id);
                if (q is not null) await SendAsync(q);
            }
            if (_offline.Count > 0 && DateTime.UtcNow >= _nextOfflineRetryUtc)
                await FlushOfflineAsync();
            WriteBackupIfDirty();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void WriteBackupIfDirty()
    {
        if (!_backupDirty || _backup is null) return;
        try
        {
            _backup.Save(SnapshotBackup());
            _backupDirty = false;
        }
        catch (Exception)
        {
            // Disk full / profile locked: keep trying on the next tick; the server copy is still authoritative.
        }
    }

    private BackupState SnapshotBackup() => new(
        _sessionId, _examId, _userId, DateTime.UtcNow,
        Questions.Where(q => q.Revision > 0 || q.OnServer)
            .Select(q => new BackupAnswer(q.QuestionId, q.ServerAnswer, q.Revision, q.AckedRevision))
            .ToList());

    /// <summary>
    /// Flushes every answer, then submits. Returns Offline (window stays open, answers safe on this PC) when an
    /// answer could not be delivered — submitting then would lock the session without it (D-19).
    /// </summary>
    public async Task<(SubmitResult Result, ReceiptView? Receipt)> SubmitAsync()
    {
        // Pause the background sync and let a running one finish, so its flush is not mistaken for "offline".
        _submitting = true;
        try
        {
            for (var waited = 0; _syncing && waited < 100; waited++) await Task.Delay(100);
            return await SubmitCoreAsync();
        }
        finally
        {
            _submitting = false;
        }
    }

    private async Task<(SubmitResult Result, ReceiptView? Receipt)> SubmitCoreAsync()
    {
        _autosave.TakeAll();   // everything with unsent edits is queued below
        foreach (var q in Questions.Where(q => q.HasUnsentEdits))
            _offline.Enqueue(new PendingSave(q.QuestionId, q.ServerAnswer, q.Revision, DateTime.UtcNow));

        _backupDirty = true;
        if (!await FlushOfflineAsync())
        {
            WriteBackupIfDirty();
            return (new SubmitResult(SubmitStatus.Offline, "Waiting for network — your answers are safe on this PC."), null);
        }
        WriteBackupIfDirty();

        var result = await _apiClient.SubmitExamAsync(_accessToken, _sessionId);
        switch (result.Status)
        {
            case SubmitStatus.Submitted when result.Receipt is not null:
            {
                var local = ReceiptHasher.Compute(_sessionId,
                    Questions.Where(q => q.OnServer).Select(q => (q.QuestionId, (string?)q.ServerAnswer)));
                var view = new ReceiptView(result.Receipt.SubmittedAt, result.Receipt.AnswerCount, result.Receipt.Sha256, local,
                    ReceiptHasher.Matches(local, result.Receipt.Sha256));
                Finish(new BackupReceipt(view.SubmittedAtUtc, view.AnswerCount, view.ServerSha256, view.LocalSha256, view.Verified));
                return (result, view);
            }
            case SubmitStatus.Submitted:
                Finish(null);
                return (result, null);
            case SubmitStatus.Failed:
            {
                // Refused: most likely already locked by a proctor or the server's timeout. Ask the server.
                var server = await _apiClient.GetSessionAnswersAsync(_accessToken, _sessionId);
                if (server is not null && !string.Equals(server.Status, "InProgress", StringComparison.OrdinalIgnoreCase))
                {
                    Finish(server.Receipt is null ? null
                        : new BackupReceipt(server.Receipt.SubmittedAt ?? DateTime.UtcNow, server.Receipt.AnswerCount, server.Receipt.Sha256, "", false));
                    return (result with { Status = SubmitStatus.AlreadyFinalised, FinalStatus = server.Status }, null);
                }
                return (result, null);
            }
            default:
                return (result, null);
        }
    }

    /// <summary>The server ended the session (force-submit / timeout): stop syncing and keep the backup as evidence.</summary>
    public void MarkFinalisedByServer()
    {
        Finish(null);
    }

    private void Finish(BackupReceipt? receipt)
    {
        Stop();
        if (_backup is null) return;
        try
        {
            _backup.Save(SnapshotBackup() with { Submitted = true, Receipt = receipt });
        }
        catch (Exception)
        {
            // best effort: an unsubmitted-looking backup only shows a resume hint, the server decides
        }
    }

    /// <summary>Resync with the server's clock (heartbeat ack): teacher extensions and per-student extra time (D-16).</summary>
    public void ResyncRemaining(int seconds)
    {
        if (_stopped) return;
        _remainingSeconds = Math.Max(0, seconds);
        RemainingTime = TimeSpan.FromSeconds(_remainingSeconds).ToString("hh\\:mm\\:ss");
        LowTime = _remainingSeconds <= 300;
    }

    public void Stop()
    {
        _stopped = true;
        _countdownTimer.Stop();
    }

    private void CountdownTick(object? sender, EventArgs e)
    {
        if (_remainingSeconds <= 0)
        {
            RemainingTime = "00:00:00";
            _countdownTimer.Stop();
            TimeExpired?.Invoke();
            return;
        }
        _remainingSeconds--;
        RemainingTime = TimeSpan.FromSeconds(_remainingSeconds).ToString("hh\\:mm\\:ss");
        LowTime = _remainingSeconds <= 300;

        _ = SyncTickAsync();
    }
}
