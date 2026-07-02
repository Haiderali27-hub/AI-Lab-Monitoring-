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
    string? AllowedApps = null
);

public record StartExamResult(Guid Id, string Status, DateTime? StartedAtUtc);

public record StudentLoginRequest(
    string UsernameOrEmail,
    string Password,
    string HardwareFingerprint,
    string? MachineName = null,      // Windows computer name of the lab PC
    string? WindowsUsername = null); // Windows account in use on that PC

public record RefreshRequest(string RefreshToken);

public record HeartbeatPayload(Guid? ExamSessionId, bool IsForegroundExamApp, string? ActiveWindowTitle, string? ProcessListSnapshot);

public record MonitoringEventPayload(string EventType, Guid? ExamSessionId, string PayloadJson);
