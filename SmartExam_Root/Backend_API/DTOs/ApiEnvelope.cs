using Backend_API.Models.Enums;

namespace Backend_API.DTOs;

public record ApiEnvelope<T>(bool Success, string Code, string Message, T? Data);

public record UserSummary(Guid Id, Guid InstitutionId, string Username, string Email, string Role);

public record TokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAtUtc,
    UserSummary User,
    bool DeviceBound
);

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
    string LockdownMode = "Report",
    bool BlockMultiMonitor = true,
    bool RequireReadiness = false,
    bool RequireWebcam = false,
    bool RequireMic = false,
    bool ReadinessPassed = false,
    DateTime? ServerTimeUtc = null,
    // Module 3: start gate, evaluated on the server clock (EarlyJoinMinutes before ExamStartUtc)
    int SecondsUntilStart = 0,
    DateTime? OpensAtUtc = null,
    int EarlyJoinMinutes = 0,
    bool CanStartNow = false,
    // Module 9: Ok | Mismatch | Unassigned | NotEnforced | NotChecked (see Services/SeatGuard)
    string? SeatStatus = null,
    // Module 8: mean capture intervals in seconds (the client jitters each wait by ±50%)
    int ScreenshotIntervalSeconds = 60,
    int WebcamIntervalSeconds = 60
);

public record PracticeExamSummary(Guid ExamId, string Title, int DurationMinutes, int QuestionCount, int Attempts, Guid? InProgressSessionId);

public record StartExamResult(Guid Id, string Status, DateTime? StartedAtUtc);

public record StudentLoginRequest(
    string UsernameOrEmail,
    string Password,
    string HardwareFingerprint,
    string? MachineName = null,      // Windows computer name of the lab PC
    string? WindowsUsername = null,  // Windows account in use on that PC
    // Module 1 HWID v2 migration: the old-format (CPU|BOARD|MAC) fingerprint, sent alongside a
    // "v2|…" fingerprint so a v1 binding can be upgraded once without an admin reset.
    string? LegacyHardwareFingerprint = null);

public record RefreshRequest(string RefreshToken);

// Module 5 adds IdleSeconds (seconds since the last keyboard/mouse input on the PC, GetLastInputInfo) and
// CameraStatus (Ok | Covered | Frozen | Unavailable | Off | NoFace | MultipleFaces | LookingAway | Phone, from on-device
// webcam monitoring). Both optional:
// older clients omit them.
public record HeartbeatPayload(Guid? ExamSessionId, bool IsForegroundExamApp, string? ActiveWindowTitle, string? ProcessListSnapshot,
    int? IdleSeconds = null, string? CameraStatus = null);

// Module 5: POST /api/monitoring/events/batch — events the desktop client queued (offline-safe) and flushes every
// 5 s, or at once for a violation. ClientEventId makes a resent batch idempotent; RecordedAt is the client's time.
public record BatchedMonitoringEvent(Guid? ClientEventId, string EventType, string? PayloadJson, DateTime? RecordedAt);

public record MonitoringEventBatch(Guid ExamSessionId, List<BatchedMonitoringEvent>? Events);

public record MonitoringEventPayload(string EventType, Guid? ExamSessionId, string PayloadJson);
