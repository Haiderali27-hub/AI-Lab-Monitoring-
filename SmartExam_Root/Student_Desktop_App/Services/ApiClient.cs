using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Student_Desktop_App.Core;
using Student_Desktop_App.Core.Auth;
using Student_Desktop_App.Core.Monitoring;
using Student_Desktop_App.Core.Submission;

namespace Student_Desktop_App.Services;

/// <summary>
/// HTTP client for the SmartExam API. Every authorized call goes through <see cref="SendAuthorizedAsync"/>:
/// the access token is read from <see cref="SessionStore"/> at send time (so a window or service that was
/// handed a token string before a refresh never sends a stale one), refreshed proactively shortly before
/// it expires, and on a 401 refreshed once (single-flight, see <see cref="TokenRefreshGate"/>) and retried.
/// The <c>accessToken</c> parameters are only a fallback for when no session is stored.
/// </summary>
public class ApiClient
{
    private readonly HttpClient _httpClient;
    private readonly TokenRefreshGate _refreshGate;
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

        _refreshGate = new TokenRefreshGate(() => SessionStore.CurrentToken, SessionStore.UpdateAccessToken, RefreshForGateAsync);
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

    /// <summary>Module 2 readiness: one timed GET /api/health, plus the server's Date header.</summary>
    public async Task<HealthProbe> ProbeHealthAsync(CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await _httpClient.GetAsync("api/health", cts.Token);
            sw.Stop();
            return new HealthProbe(response.IsSuccessStatusCode, sw.Elapsed, response.Headers.Date);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthProbe(false, sw.Elapsed, null);
        }
    }

    // Plain JSON response (no ApiEnvelope).
    public async Task<(bool ok, bool passedAll, string message)> SubmitReadinessAsync(
        string accessToken,
        Guid examId,
        object payload,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendAuthorizedAsync(
                token => CreateAuthorizedJsonRequest(HttpMethod.Post, $"api/exams/{examId}/readiness", token, payload),
                accessToken, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) return (false, false, $"Server rejected the check (HTTP {(int)response.StatusCode}).");
            using var doc = JsonDocument.Parse(body);
            var passed = doc.RootElement.TryGetProperty("passedAll", out var p) && p.GetBoolean();
            return (true, passed, passed ? "System check passed." : "System check failed.");
        }
        catch (Exception ex)
        {
            return (false, false, ex.Message);
        }
    }

    public Task<ApiResult<List<PracticeExamSummary>>> GetPracticeExamsAsync(
        string accessToken,
        CancellationToken cancellationToken = default) =>
        SendWithRefreshAsync<List<PracticeExamSummary>>(
            token => CreateAuthorizedRequest(HttpMethod.Get, "api/exams/student/practice", token), accessToken, cancellationToken);

    // start-session returns a plain object { sessionId } or { code, message } (no ApiEnvelope).
    public async Task<(Guid? sessionId, string message)> StartSessionAsync(
        string accessToken,
        Guid examId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendAuthorizedAsync(
                token => CreateAuthorizedJsonRequest(HttpMethod.Post, $"api/exams/{examId}/start-session", token, new { }),
                accessToken, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            if (response.IsSuccessStatusCode && doc.RootElement.TryGetProperty("sessionId", out var sid))
                return (sid.GetGuid(), "Started.");
            return (null, doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "Could not start." : $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    /// <param name="hardwareFingerprint">The current ("v2|BIOS|DISK|BOARD") fingerprint.</param>
    /// <param name="legacyHardwareFingerprint">The old-format fingerprint, so the server can upgrade a v1 binding once.</param>
    public async Task<ApiResult<TokenResponse>> StudentLoginAsync(
        string usernameOrEmail,
        string password,
        string hardwareFingerprint,
        string? legacyHardwareFingerprint = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            usernameOrEmail,
            password,
            hardwareFingerprint,
            legacyHardwareFingerprint,
            // Human-readable machine identity so proctors/admins can see WHO is on WHICH PC
            machineName = Environment.MachineName,
            windowsUsername = Environment.UserName
        };

        using var request = CreateJsonRequest(HttpMethod.Post, "api/auth/student-login", payload);
        return await SendAsync<TokenResponse>(request, cancellationToken);
    }

    public Task<ApiResult<StudentExamStatus>> GetCurrentExamStatusAsync(
        string accessToken,
        CancellationToken cancellationToken = default) =>
        SendWithRefreshAsync<StudentExamStatus>(
            token => CreateAuthorizedRequest(HttpMethod.Get, "api/exams/student/current", token), accessToken, cancellationToken);

    public Task<ApiResult<StartExamResult>> StartExamAsync(
        string accessToken,
        CancellationToken cancellationToken = default) =>
        SendWithRefreshAsync<StartExamResult>(
            token => CreateAuthorizedJsonRequest(HttpMethod.Post, "api/exams/student/start", token, new { }), accessToken, cancellationToken);

    public Task<ApiResult<HeartbeatAck>> SendHeartbeatAsync(
        string accessToken,
        HeartbeatPayload payload,
        CancellationToken cancellationToken = default) =>
        SendWithRefreshAsync<HeartbeatAck>(
            token => CreateAuthorizedJsonRequest(HttpMethod.Post, "api/monitoring/heartbeat", token, payload), accessToken, cancellationToken);

    // Exam detail (questions) for the student's exam-taking window.
    // This endpoint returns a plain object (no ApiEnvelope), so deserialize directly.
    public async Task<ExamDetailDto?> GetExamDetailAsync(
        string accessToken,
        Guid examId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendAuthorizedAsync(
                token => CreateAuthorizedRequest(HttpMethod.Get, $"api/exams/{examId}", token), accessToken, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<ExamDetailDto>(body, _jsonSerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    // Module 6: one revisioned answer save (plain JSON body, no ApiEnvelope). Never throws: the outcome tells the
    // exam window whether the server has the answer (Accepted / Stale), should be retried (Offline) or refused it
    // for good (Rejected — time over, session finished); Message is the server's reason for a rejection.
    public async Task<(SaveOutcome Outcome, string Message)> SaveAnswerAsync(
        string accessToken,
        Guid sessionId,
        PendingSave save,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                questionId = save.QuestionId,
                answerText = save.Text,
                revision = save.Revision,
                clientSavedAt = save.ClientSavedAtUtc
            };
            using var response = await SendAuthorizedAsync(
                token => CreateAuthorizedJsonRequest(HttpMethod.Post, $"api/exams/sessions/{sessionId}/save-answer", token, payload),
                accessToken, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var (code, message) = ReadCodeAndMessage(body);
            return (OfflineSaveQueue.Classify((int)response.StatusCode, code), message ?? $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            // Network down, timeout, or an unreadable reply: keep the answer and retry later.
            return (SaveOutcome.Offline, ex.Message);
        }
    }

    // Plain JSON body: { message, sessionId, submittedAt, answerCount, sha256 } on success, { message, status } when refused.
    public async Task<SubmitResult> SubmitExamAsync(
        string accessToken,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendAuthorizedAsync(
                token => CreateAuthorizedJsonRequest(HttpMethod.Post, $"api/exams/sessions/{sessionId}/submit", token, new { }),
                accessToken, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var (_, message) = ReadCodeAndMessage(body);
            var status = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                var receipt = JsonSerializer.Deserialize<SubmitReceipt>(body, _jsonSerializerOptions);
                return new SubmitResult(SubmitStatus.Submitted, message ?? "Exam submitted.", receipt);
            }
            if (status is 401 or 408 or 429 || status >= 500)
                return new SubmitResult(SubmitStatus.Offline, message ?? $"HTTP {status}");
            return new SubmitResult(SubmitStatus.Failed, message ?? $"HTTP {status}");
        }
        catch (Exception ex)
        {
            // A retry is safe: if the server did lock the session, the next attempt reports it as finalised.
            return new SubmitResult(SubmitStatus.Offline, ex.Message);
        }
    }

    // Module 6 crash recovery: the server's copy of this session's answers (null when unreachable).
    public async Task<SessionAnswersDto?> GetSessionAnswersAsync(
        string accessToken,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendAuthorizedAsync(
                token => CreateAuthorizedRequest(HttpMethod.Get, $"api/exams/sessions/{sessionId}/answers", token), accessToken, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<SessionAnswersDto>(body, _jsonSerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private static (string? Code, string? Message) ReadCodeAndMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return (null, null);
            var code = doc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            var message = doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            return (code, message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    public Task<ApiResult<object>> SendMonitoringEventAsync(
        string accessToken,
        string eventType,
        Guid? examSessionId,
        object payload,
        CancellationToken cancellationToken = default)
    {
        var envelope = new MonitoringEventPayload(eventType, examSessionId, JsonSerializer.Serialize(payload));
        return SendWithRefreshAsync<object>(
            token => CreateAuthorizedJsonRequest(HttpMethod.Post, "api/monitoring/event", token, envelope), accessToken, cancellationToken);
    }

    /// <summary>
    /// Module 5: delivers queued monitoring events in one call (POST /api/monitoring/events/batch). Idempotent on the
    /// server (clientEventId), so a batch whose reply was lost is simply sent again. Never throws.
    /// </summary>
    public async Task<DeliveryOutcome> SendEventBatchAsync(
        string accessToken,
        Guid sessionId,
        IReadOnlyList<QueuedEvent> events,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                examSessionId = sessionId,
                events = events.Select(e => new
                {
                    clientEventId = e.ClientEventId,
                    eventType = e.EventType,
                    payloadJson = e.PayloadJson,
                    recordedAt = e.RecordedAtUtc
                }).ToList()
            };
            using var response = await SendAuthorizedAsync(
                token => CreateAuthorizedJsonRequest(HttpMethod.Post, "api/monitoring/events/batch", token, payload),
                accessToken, cancellationToken);
            return Delivery.Classify((int)response.StatusCode);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return DeliveryOutcome.Offline;
        }
    }

    /// <summary>
    /// Module 5: uploads one screen capture or webcam snapshot (multipart POST /api/monitoring/screenshot).
    /// Rejected (drop it) when the server refuses it for good — e.g. 409 once the session is over. Never throws.
    /// </summary>
    public async Task<DeliveryOutcome> UploadCaptureAsync(
        string accessToken,
        Guid sessionId,
        CaptureInfo info,
        byte[] image,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendAuthorizedAsync(token =>
            {
                var request = CreateAuthorizedRequest(HttpMethod.Post, "api/monitoring/screenshot", token);
                var form = new MultipartFormDataContent
                {
                    { new StringContent(sessionId.ToString()), "examSessionId" },
                    { new StringContent(info.Kind), "kind" },
                    { new StringContent(info.Trigger), "trigger" },
                    { new StringContent(info.CapturedAtUtc.ToString("O")), "capturedAt" }
                };
                if (!string.IsNullOrWhiteSpace(info.Reason)) form.Add(new StringContent(info.Reason), "reason");
                var file = new ByteArrayContent(image);
                file.Headers.ContentType = new MediaTypeHeaderValue(info.ContentType);
                form.Add(file, "file", info.ContentType == "image/png" ? "capture.png" : "capture.jpg");
                request.Content = form;
                return request;
            }, accessToken, cancellationToken);
            return Delivery.Classify((int)response.StatusCode);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return DeliveryOutcome.Offline;
        }
    }

    /// <summary>
    /// True while the session is still valid server-side. Returns false ONLY when the server has
    /// explicitly ended it: a 401 that a refresh cannot fix (revoked / force-logout / deactivated).
    /// An expired access token is renewed transparently, and network errors return true so a Wi-Fi
    /// blip can't kick a student out of an exam.
    /// </summary>
    public async Task<bool> IsSessionAliveAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var token = await _refreshGate.GetAccessTokenAsync(cancellationToken) ?? accessToken;
            using (var request = CreateAuthorizedRequest(HttpMethod.Get, "api/auth/me", token))
            using (var response = await _httpClient.SendAsync(request, cancellationToken))
            {
                if (response.StatusCode != HttpStatusCode.Unauthorized) return true;
            }

            if (SessionStore.CurrentToken is null) return false;
            var attempt = await _refreshGate.RefreshAsync(token, cancellationToken);
            if (attempt.Status == RefreshStatus.Unavailable) return true;
            if (attempt.Status == RefreshStatus.Rejected || attempt.Token is null) return false;

            using var retry = CreateAuthorizedRequest(HttpMethod.Get, "api/auth/me", attempt.Token.AccessToken);
            using var retried = await _httpClient.SendAsync(retry, cancellationToken);
            return retried.StatusCode != HttpStatusCode.Unauthorized;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Fetch this student's notifications (teacher warnings/announcements).
    /// This endpoint returns a plain JSON object (no ApiEnvelope), so it is
    /// deserialized directly. Returns null on any failure.
    /// </summary>
    public async Task<NotificationsEnvelope?> GetNotificationsAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendAuthorizedAsync(
                token => CreateAuthorizedRequest(HttpMethod.Get, "api/notifications", token), accessToken, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<NotificationsEnvelope>(body, _jsonSerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task MarkNotificationReadAsync(
        string accessToken,
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var _ = await SendAuthorizedAsync(
                token => CreateAuthorizedRequest(HttpMethod.Patch, $"api/notifications/{notificationId}/read", token),
                accessToken, cancellationToken);
        }
        catch
        {
            // best-effort — an unread flag is not worth crashing the exam client
        }
    }

    /// <summary>Server-side logout: revokes this session and its refresh-token family.</summary>
    public Task<ApiResult<object>> LogoutAsync(
        string accessToken,
        CancellationToken cancellationToken = default) =>
        SendWithRefreshAsync<object>(
            token => CreateAuthorizedJsonRequest(HttpMethod.Post, "api/auth/logout", token, new { }), accessToken, cancellationToken);

    public async Task<ApiResult<TokenResponse>> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var payload = new { refreshToken };
        using var request = CreateJsonRequest(HttpMethod.Post, "api/auth/refresh", payload);
        return await SendAsync<TokenResponse>(request, cancellationToken);
    }

    // Maps the refresh call onto the gate's three outcomes. Only an explicit refusal ends the session;
    // network errors, rate limiting (429) and server errors leave the current token in place.
    private async Task<RefreshAttempt> RefreshForGateAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var result = await RefreshAsync(refreshToken, cancellationToken);
        if (result.Success && result.Data is not null) return new RefreshAttempt(RefreshStatus.Refreshed, result.Data);
        return IsTransientFailure(result.Code) ? RefreshAttempt.Unavailable : RefreshAttempt.Rejected;
    }

    public static bool IsTransientFailure(string code) =>
        code == "NETWORK_ERROR" || code == "HTTP_429" || code == "HTTP_408" ||
        (code.StartsWith("HTTP_5", StringComparison.Ordinal) && code.Length == 8);

    private HttpRequestMessage CreateJsonRequest(HttpMethod method, string path, object payload)
    {
        var request = new HttpRequestMessage(method, path);
        var json = JsonSerializer.Serialize(payload);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static HttpRequestMessage CreateAuthorizedJsonRequest(HttpMethod method, string path, string accessToken, object payload)
    {
        var request = CreateAuthorizedRequest(method, path, accessToken);
        var json = JsonSerializer.Serialize(payload);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    /// <summary>
    /// Sends an authorized request built by <paramref name="build"/> for a given access token. Uses the
    /// stored session token (refreshed first when near expiry); on a 401 refreshes once and rebuilds +
    /// resends the request. Throws on network errors like <see cref="HttpClient.SendAsync(HttpRequestMessage, CancellationToken)"/>.
    /// </summary>
    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        Func<string, HttpRequestMessage> build, string fallbackAccessToken, CancellationToken cancellationToken)
    {
        var token = await _refreshGate.GetAccessTokenAsync(cancellationToken) ?? fallbackAccessToken;

        HttpResponseMessage response;
        using (var request = build(token))
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }

        if (response.StatusCode != HttpStatusCode.Unauthorized || SessionStore.CurrentToken is null)
        {
            return response;
        }

        var attempt = await _refreshGate.RefreshAsync(token, cancellationToken);
        if (attempt.Status != RefreshStatus.Refreshed || attempt.Token is null)
        {
            return response;
        }

        response.Dispose();
        using var retry = build(attempt.Token.AccessToken);
        return await _httpClient.SendAsync(retry, cancellationToken);
    }

    private async Task<ApiResult<T>> SendWithRefreshAsync<T>(
        Func<string, HttpRequestMessage> build, string fallbackAccessToken, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAuthorizedAsync(build, fallbackAccessToken, cancellationToken);
            return await ParseAsync<T>(response, cancellationToken);
        }
        catch (Exception ex)
        {
            return ApiResult<T>.Fail("NETWORK_ERROR", ex.Message);
        }
    }

    private async Task<ApiResult<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return await ParseAsync<T>(response, cancellationToken);
        }
        catch (Exception ex)
        {
            return ApiResult<T>.Fail("NETWORK_ERROR", ex.Message);
        }
    }

    private async Task<ApiResult<T>> ParseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(body))
        {
            return response.IsSuccessStatusCode
                ? new ApiResult<T>(true, "SUCCESS", "Success", default)
                : ApiResult<T>.Fail($"HTTP_{(int)response.StatusCode}", response.ReasonPhrase ?? "Request failed");
        }

        var envelope = JsonSerializer.Deserialize<ApiEnvelope<T>>(body, _jsonSerializerOptions);
        if (envelope is not null && !string.IsNullOrEmpty(envelope.Code))
        {
            return new ApiResult<T>(envelope.Success, envelope.Code, envelope.Message, envelope.Data);
        }

        return response.IsSuccessStatusCode
            ? new ApiResult<T>(true, "SUCCESS", "Success", JsonSerializer.Deserialize<T>(body, _jsonSerializerOptions))
            : ApiResult<T>.Fail($"HTTP_{(int)response.StatusCode}", body);
    }
}
