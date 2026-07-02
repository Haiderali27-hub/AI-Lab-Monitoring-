using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Threading;
using Student_Desktop_App.Core;
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
}

public class ExamSessionViewModel : BindableBase
{
    private readonly ApiClient _apiClient;
    private readonly string _accessToken;
    private readonly Guid _sessionId;
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _remainingSeconds;

    private string _examTitle = "Exam";
    private string _remainingTime = "00:00:00";
    private bool _lowTime;
    private string _saveStatus = "";
    private int _answeredCount;

    public ExamSessionViewModel(ApiClient apiClient, string accessToken, Guid sessionId, int remainingSeconds)
    {
        _apiClient = apiClient;
        _accessToken = accessToken;
        _sessionId = sessionId;
        _remainingSeconds = Math.Max(0, remainingSeconds);
        RemainingTime = TimeSpan.FromSeconds(_remainingSeconds).ToString("hh\\:mm\\:ss");
    }

    public ObservableCollection<ExamQuestionItem> Questions { get; } = new();
    public event Action? TimeExpired;

    public string ExamTitle { get => _examTitle; set => SetProperty(ref _examTitle, value); }
    public string RemainingTime { get => _remainingTime; set => SetProperty(ref _remainingTime, value); }
    public bool LowTime { get => _lowTime; set => SetProperty(ref _lowTime, value); }
    public string SaveStatus { get => _saveStatus; set => SetProperty(ref _saveStatus, value); }

    public int AnsweredCount { get => _answeredCount; set { if (SetProperty(ref _answeredCount, value)) OnPropertyChanged(nameof(ProgressLabel)); } }
    public string ProgressLabel => $"{AnsweredCount} of {Questions.Count} answered";

    public async Task LoadAsync(Guid examId)
    {
        var detail = await _apiClient.GetExamDetailAsync(_accessToken, examId);
        if (detail is null) return;

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

        _countdownTimer.Tick += CountdownTick;
        _countdownTimer.Start();
    }

    public void RecomputeAnswered()
    {
        AnsweredCount = Questions.Count(q => q.IsAnswered);
    }

    // Persist a single question's answer (called as the student answers).
    public async Task SaveAnswerAsync(ExamQuestionItem q)
    {
        if (!q.IsAnswered) return;
        SaveStatus = "Saving…";
        var ok = await _apiClient.SaveAnswerAsync(_accessToken, _sessionId, q.QuestionId, q.ServerAnswer);
        SaveStatus = ok ? $"Saved {DateTime.Now:T}" : "Save failed — will retry on submit";
        RecomputeAnswered();
    }

    // Save everything then submit.
    public async Task<(bool ok, string message)> SubmitAsync()
    {
        foreach (var q in Questions.Where(q => q.IsAnswered))
            await _apiClient.SaveAnswerAsync(_accessToken, _sessionId, q.QuestionId, q.ServerAnswer);

        var (ok, message) = await _apiClient.SubmitExamAsync(_accessToken, _sessionId);
        if (ok) _countdownTimer.Stop();
        return (ok, message);
    }

    public void Stop() => _countdownTimer.Stop();

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
    }
}
