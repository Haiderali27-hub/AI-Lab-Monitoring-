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
    string? AllowedApps = null,
    string? LockdownMode = null,
    bool? BlockMultiMonitor = null,
    bool RequireReadiness = false,
    bool RequireWebcam = false,
    bool RequireMic = false,
    bool ReadinessPassed = false,
    DateTime? ServerTimeUtc = null,
    // Module 3 start gate (server clock) and Module 9 seat check — see Core/Dashboard/StartGate
    int SecondsUntilStart = 0,
    DateTime? OpensAtUtc = null,
    int EarlyJoinMinutes = 0,
    bool? CanStartNow = null,
    string? SeatStatus = null,
    // Module 8: mean capture intervals (s) for the Module 5 capture service; it jitters each wait by ±50%.
    int ScreenshotIntervalSeconds = 60,
    int WebcamIntervalSeconds = 60);

public record HealthProbe(bool Ok, TimeSpan RoundTrip, DateTimeOffset? ServerTime);

public record PracticeExamSummary(Guid ExamId, string Title, int DurationMinutes, int QuestionCount, int Attempts, Guid? InProgressSessionId);

public record StartExamResult(Guid Id, string Status, DateTime? StartedAtUtc);

// Module 5: IdleSeconds = time since the last keyboard/mouse input (GetLastInputInfo); CameraStatus = Off | Ok |
// Covered | Frozen | Unavailable | NoFace | MultipleFaces | LookingAway | Phone from on-device webcam monitoring.
public record HeartbeatPayload(Guid? ExamSessionId, bool IsForegroundExamApp, string? ActiveWindowTitle, string? ProcessListSnapshot,
    int? IdleSeconds = null, string? CameraStatus = null);

// Server ack for a heartbeat; RemainingSeconds is the authoritative exam clock (lets the countdown resync
// after a teacher extends the exam or a proctor grants this student extra minutes). SessionStatus tells the
// exam window that a proctor force-submitted the session or the server timed it out (Module 6, D-17).
// The capture settings let a running client follow a teacher's change (Module 5).
// Module 10: the ack is also this client's command channel (no SignalR here): the proctors' unread warnings (shown on
// the exam window until acknowledged, D-05) and the lockdown unlock deadline in server time (null = locked).
public record HeartbeatAck(DateTime ReceivedAtUtc, int? RemainingSeconds, string? SessionStatus = null, int? ExtraMinutes = null,
    int? ScreenshotIntervalSeconds = null, int? WebcamIntervalSeconds = null, bool? RequireWebcam = null,
    DateTime? UnlockedUntilUtc = null, List<Student_Desktop_App.Core.Monitoring.ProctorWarning>? Warnings = null);

/// <summary>Module 5: what the capture service needs to know about the exam (StudentExamStatus / heartbeat ack).</summary>
public record CaptureSettings(int ScreenshotIntervalSeconds = 60, int WebcamIntervalSeconds = 60, bool RequireWebcam = false);

// Module 6: the receipt returned by POST /api/exams/sessions/{id}/submit (flat body).
public record SubmitReceipt(Guid SessionId, DateTime SubmittedAt, int AnswerCount, string Sha256);

public enum SubmitStatus { Submitted, Offline, AlreadyFinalised, Failed }

/// <summary>Result of a submit attempt. AlreadyFinalised = the server had already locked the session
/// (proctor force-submit / timeout); FinalStatus then carries the server's status.</summary>
public record SubmitResult(SubmitStatus Status, string Message, SubmitReceipt? Receipt = null, string? FinalStatus = null);

// Module 6: GET /api/exams/sessions/{id}/answers (crash recovery).
public record SessionAnswerDto(Guid QuestionId, string AnswerText, int Revision, DateTime LastSavedAt);

public record SessionReceiptDto(DateTime? SubmittedAt, int AnswerCount, string Sha256);

public record SessionAnswersDto(Guid SessionId, string Status, int RemainingSeconds, int ExtraMinutes, SessionReceiptDto? Receipt, List<SessionAnswerDto> Answers);

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