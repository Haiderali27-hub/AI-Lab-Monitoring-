using System.ComponentModel.DataAnnotations;

namespace Backend_API.Contracts;

public record HeartbeatRequest(
    Guid? ExamSessionId,
    bool IsForegroundExamApp,
    string? ActiveWindowTitle,
    string? ProcessListSnapshot);

public record MonitoringEventRequest(
    [Required, MaxLength(80)] string EventType,
    Guid? ExamSessionId,
    [Required] string PayloadJson);
