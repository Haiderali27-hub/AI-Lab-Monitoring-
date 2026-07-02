using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Student_Desktop_App.Services;
using Student_Desktop_App.ViewModels;

namespace Student_Desktop_App.Views;

public partial class ExamWindow : Window
{
    private readonly ExamSessionViewModel _viewModel;
    private readonly Guid _examId;
    private ExamQuestionItem? _current;
    private bool _submitted;
    private bool _suppressTextEvents;

    public bool Submitted => _submitted;

    public ExamWindow(ApiClient apiClient, string accessToken, Guid examId, Guid sessionId, int remainingSeconds)
    {
        InitializeComponent();
        _examId = examId;
        _viewModel = new ExamSessionViewModel(apiClient, accessToken, sessionId, remainingSeconds);
        _viewModel.TimeExpired += () => Dispatcher.Invoke(async () => await AutoSubmitOnExpiry());
        DataContext = _viewModel;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync(_examId);
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
        _viewModel.RecomputeAnswered();
    }

    private void AnswerBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextEvents || _current is null) return;
        _current.AnswerText = AnswerBox.Text;
        _viewModel.RecomputeAnswered();
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
        if (MessageBox.Show(prompt, "Submit Exam", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await DoSubmit(auto: false);
    }

    private async Task AutoSubmitOnExpiry()
    {
        MessageBox.Show("Time is up. Your exam is being submitted automatically.", "Time Expired", MessageBoxButton.OK, MessageBoxImage.Information);
        await DoSubmit(auto: true);
    }

    private async Task DoSubmit(bool auto)
    {
        if (_submitted) return;
        SubmitButton.IsEnabled = false;
        if (_current is not null) await _viewModel.SaveAnswerAsync(_current);

        var (ok, message) = await _viewModel.SubmitAsync();
        if (!ok && !auto)
        {
            SubmitButton.IsEnabled = true;
            MessageBox.Show($"Could not submit: {message}", "Submit Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _submitted = true;
        MessageBox.Show("Your exam has been submitted successfully.", "Submitted", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _viewModel.Stop();
    }
}
