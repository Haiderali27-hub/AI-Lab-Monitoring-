namespace Student_Desktop_App.Services;

public record ApiEnvelope<T>(bool Success, string Code, string Message, T? Data);

public record ApiResult<T>(bool Success, string Code, string Message, T? Data)
{
    public static ApiResult<T> Fail(string code, string message) => new(false, code, message, default);
}

public record UserSummary(Guid Id, Guid InstitutionId, string Username, string Email, string Role);

public record TokenResponse(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAtUtc, UserSummary User, bool DeviceBound);

public record StudentExamStatus(
    Guid? ExamId,
    string ExamName,
    string Status,
    DateTime? ExamStartUtc,
    DateTime? ExamEndUtc,
    int RemainingSeconds,
    bool IsEligible,
    string Message);

public record StartExamResult(Guid Id, string Status, DateTime? StartedAtUtc);

public record HeartbeatPayload(Guid? ExamSessionId, bool IsForegroundExamApp, string? ActiveWindowTitle, string? ProcessListSnapshot);