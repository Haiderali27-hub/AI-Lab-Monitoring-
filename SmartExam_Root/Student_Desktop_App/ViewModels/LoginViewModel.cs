using Student_Desktop_App.Core;
using Student_Desktop_App.Services;

namespace Student_Desktop_App.ViewModels;

public class LoginViewModel(ApiClient apiClient) : BindableBase
{
    private readonly ApiClient _apiClient = apiClient;

    private string _usernameOrEmail = string.Empty;
    private string _statusMessage = "Enter credentials to continue.";
    private string _connectionStatus = "Checking...";
    private bool _isBusy;

    public string UsernameOrEmail
    {
        get => _usernameOrEmail;
        set => SetProperty(ref _usernameOrEmail, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set => SetProperty(ref _connectionStatus, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public async Task CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        ConnectionStatus = "Checking server connection...";
        var connected = await _apiClient.CheckConnectionAsync(cancellationToken);
        ConnectionStatus = connected
            ? "Connected to server"
            : "Server unreachable (ensure LAN/local API is running)";
    }

    public async Task<bool> LoginAsync(string password, CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(UsernameOrEmail) || string.IsNullOrWhiteSpace(password))
        {
            StatusMessage = "Username/email and password are required.";
            return false;
        }

        IsBusy = true;
        try
        {
            StatusMessage = "Authenticating...";

            var hardwareFingerprint = DeviceFingerprint.CreateRawFingerprint();
            var result = await _apiClient.StudentLoginAsync(UsernameOrEmail.Trim(), password, hardwareFingerprint, cancellationToken);
            if (!result.Success || result.Data is null)
            {
                StatusMessage = result.Message;
                return false;
            }

            SessionStore.Set(result.Data);
            StatusMessage = "Login successful.";
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }
}