using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Student_Desktop_App.Core;

namespace Student_Desktop_App.Services;

public class ApiClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiClient()
    {
        var allowInsecureHttps = bool.TryParse(Environment.GetEnvironmentVariable("SMARTEXAM_ALLOW_INSECURE_HTTPS"), out var flag) && flag;

        var handler = new HttpClientHandler();
        if (allowInsecureHttps)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(AppConfigProvider.Current.ApiBaseUrl.TrimEnd('/') + "/")
        };
    }

    public async Task<bool> CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("api/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ApiResult<TokenResponse>> StudentLoginAsync(
        string usernameOrEmail,
        string password,
        string hardwareFingerprint,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            usernameOrEmail,
            password,
            hardwareFingerprint
        };

        using var request = CreateJsonRequest(HttpMethod.Post, "api/auth/student-login", payload);
        return await SendAsync<TokenResponse>(request, cancellationToken);
    }

    public async Task<ApiResult<StudentExamStatus>> GetCurrentExamStatusAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/exams/student/current", accessToken);
        return await SendWithRefreshAsync<StudentExamStatus>(request, cancellationToken);
    }

    public async Task<ApiResult<StartExamResult>> StartExamAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/exams/student/start", accessToken);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        return await SendWithRefreshAsync<StartExamResult>(request, cancellationToken);
    }

    public async Task<ApiResult<object>> SendHeartbeatAsync(
        string accessToken,
        HeartbeatPayload payload,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedJsonRequest(HttpMethod.Post, "api/monitoring/heartbeat", accessToken, payload);
        return await SendWithRefreshAsync<object>(request, cancellationToken);
    }

    public async Task<ApiResult<object>> SendMonitoringEventAsync(
        string accessToken,
        string eventType,
        Guid? examSessionId,
        object payload,
        CancellationToken cancellationToken = default)
    {
        var envelope = new MonitoringEventPayload(eventType, examSessionId, JsonSerializer.Serialize(payload));
        using var request = CreateAuthorizedJsonRequest(HttpMethod.Post, "api/monitoring/event", accessToken, envelope);
        return await SendWithRefreshAsync<object>(request, cancellationToken);
    }

    public async Task<ApiResult<object>> LogoutAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/auth/logout", accessToken);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        return await SendWithRefreshAsync<object>(request, cancellationToken);
    }

    public async Task<ApiResult<TokenResponse>> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var payload = new { refreshToken };
        using var request = CreateJsonRequest(HttpMethod.Post, "api/auth/refresh", payload);
        return await SendAsync<TokenResponse>(request, cancellationToken);
    }

    private HttpRequestMessage CreateJsonRequest(HttpMethod method, string path, object payload)
    {
        var request = new HttpRequestMessage(method, path);
        var json = JsonSerializer.Serialize(payload);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private HttpRequestMessage CreateAuthorizedJsonRequest(HttpMethod method, string path, string accessToken, object payload)
    {
        var request = CreateAuthorizedRequest(method, path, accessToken);
        var json = JsonSerializer.Serialize(payload);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private async Task<ApiResult<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(body))
            {
                return response.IsSuccessStatusCode
                    ? new ApiResult<T>(true, "SUCCESS", "Success", default)
                    : ApiResult<T>.Fail($"HTTP_{(int)response.StatusCode}", response.ReasonPhrase ?? "Request failed");
            }

            var envelope = JsonSerializer.Deserialize<ApiEnvelope<T>>(body, _jsonSerializerOptions);
            if (envelope is not null)
            {
                return new ApiResult<T>(envelope.Success, envelope.Code, envelope.Message, envelope.Data);
            }

            return response.IsSuccessStatusCode
                ? new ApiResult<T>(true, "SUCCESS", "Success", JsonSerializer.Deserialize<T>(body, _jsonSerializerOptions))
                : ApiResult<T>.Fail($"HTTP_{(int)response.StatusCode}", body);
        }
        catch (Exception ex)
        {
            return ApiResult<T>.Fail("NETWORK_ERROR", ex.Message);
        }
    }

    private async Task<ApiResult<T>> SendWithRefreshAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var result = await SendAsync<T>(request, cancellationToken);
        if (result.Code != "HTTP_401")
        {
            return result;
        }

        var token = SessionStore.CurrentToken;
        if (token is null || string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            return result;
        }

        var refreshResult = await RefreshAsync(token.RefreshToken, cancellationToken);
        if (!refreshResult.Success || refreshResult.Data is null)
        {
            return result;
        }

        SessionStore.UpdateAccessToken(refreshResult.Data);

        var retryRequest = CloneRequestWithNewToken(request, refreshResult.Data.AccessToken);
        return await SendAsync<T>(retryRequest, cancellationToken);
    }

    private static HttpRequestMessage CloneRequestWithNewToken(HttpRequestMessage request, string accessToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Content = request.Content,
            Version = request.Version
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        clone.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return clone;
    }
}