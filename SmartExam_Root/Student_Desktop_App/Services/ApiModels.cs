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
    string Message,
    string? Instructions,
    string? LabName,
    string? ProctorName,
    string? Seat = null,
    string? AllowedApps = null);

public record StartExamResult(Guid Id, string Status, DateTime? StartedAtUtc);

public record HeartbeatPayload(Guid? ExamSessionId, bool IsForegroundExamApp, string? ActiveWindowTitle, string? ProcessListSnapshot);

// Server ack for a heartbeat; RemainingSeconds is the authoritative exam clock
// (lets the countdown resync after a teacher grants a time extension).
public record HeartbeatAck(DateTime ReceivedAtUtc, int? RemainingSeconds);

public record MonitoringEventPayload(string EventType, Guid? ExamSessionId, string PayloadJson);

public record NotificationItem(Guid NotificationId, string Title, string Body, string Type, bool IsRead, DateTime CreatedAt);

public record NotificationsEnvelope(int UnreadCount, List<NotificationItem> Notifications);

// Exam detail as returned to a student (answer keys / hidden test cases already stripped server-side).
public record ExamTestCaseDto(Guid TestCaseId, string Input, string ExpectedOutput, bool IsHidden);

public record ExamQuestionDto(
    Guid QuestionId,
    string Type,
    string BodyText,
    int Marks,
    int OrderIndex,
    string? OptionsJson,
    List<ExamTestCaseDto>? TestCases);

public record ExamDetailDto(Guid ExamId, string Title, List<ExamQuestionDto> Questions);