using System.Windows;
using Student_Desktop_App.Services;
using Student_Desktop_App.ViewModels;

namespace Student_Desktop_App.Views;

public partial class LoginWindow : Window
{
    private readonly ApiClient _apiClient = new();
    private readonly LoginViewModel _viewModel;

    public LoginWindow()
    {
        InitializeComponent();
        _viewModel = new LoginViewModel(_apiClient);
        DataContext = _viewModel;
        
        // Allow dragging the window
        this.MouseLeftButtonDown += (s, e) => {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        };
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.CheckConnectionAsync();
    }

    private async void LoginButton_OnClick(object sender, RoutedEventArgs e)
    {
        LoginButton.IsEnabled = false;
        try
        {
            var success = await _viewModel.LoginAsync(PasswordInput.Password);
            if (!success)
            {
                return;
            }

            var dashboard = new ExamDashboard(_apiClient);
            dashboard.Show();
            Close();
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }
}