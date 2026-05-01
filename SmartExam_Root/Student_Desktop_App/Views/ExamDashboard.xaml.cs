using System.ComponentModel;
using System.Windows;
using Student_Desktop_App.Services;
using Student_Desktop_App.ViewModels;

namespace Student_Desktop_App.Views;

public partial class ExamDashboard : Window
{
    private readonly ApiClient _apiClient;
    private readonly DashboardViewModel _viewModel;
    private readonly LiveMonitoringService _monitoringService;

    public ExamDashboard(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _viewModel = new DashboardViewModel(_apiClient);
        _monitoringService = new LiveMonitoringService(_apiClient);
        _monitoringService.StatusChanged += status => Dispatcher.Invoke(() => _viewModel.UpdateMonitoringStatus(status));
        DataContext = _viewModel;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        StartExamButton.IsEnabled = _viewModel.CanStartExam;
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
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
        }
    }

    private async void LogoutButton_OnClick(object sender, RoutedEventArgs e)
    {
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
        _monitoringService.Stop();
    }
}