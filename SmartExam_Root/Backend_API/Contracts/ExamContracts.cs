using Backend_API.Models;

namespace Backend_API.Contracts;

public record StudentExamStatusDto(
    Guid? ExamId,
    string ExamName,
    ExamSessionStatus Status,
    DateTime? ExamStartUtc,
    DateTime? ExamEndUtc,
    int RemainingSeconds,
    bool IsEligible,
    string Message);

public record LiveRosterItemDto(
    Guid StudentId,
    string Username,
    ExamSessionStatus Status,
    int RemainingSeconds,
    DateTime? LastHeartbeatUtc,
    bool IsOnline);