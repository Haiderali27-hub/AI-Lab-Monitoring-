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
    string Message,
    string? Instructions,
    string? LabName,
    string? ProctorName);

public record ExamSummaryDto(
    Guid Id,
    string Name,
    DateTime StartUtc,
    DateTime EndUtc,
    string Status,
    bool IsActive,
    bool IsCancelled,
    bool IsArchived,
    string? LabName,
    Guid? LabId,
    string? ProctorName,
    Guid? ProctorUserId,
    int CandidateCount,
    int EligibleCount,
    int IneligibleCount,
    string? Instructions);

public record ExamCandidateDto(
    Guid Id,
    string Username,
    string Email,
    bool HasBinding,
    bool IsActive);

public record ProctorDto(
    Guid Id,
    string Username,
    string Email);

public record ExamAssignmentRequest(
    Guid StudentId,
    bool IsEligible,
    Guid? WorkstationId = null);

public record ExamAssignmentDetailDto(
    Guid AssignmentId,
    Guid StudentId,
    string Username,
    string Email,
    bool IsEligible,
    Guid? WorkstationId,
    string? WorkstationName);

public record CreateExamRequest(
    string Name,
    DateTime StartUtc,
    DateTime EndUtc,
    Guid? LabId,
    Guid? ProctorUserId,
    string? Instructions,
    IReadOnlyList<ExamAssignmentRequest> Assignments);

public record UpdateExamRequest(
    string Name,
    DateTime StartUtc,
    DateTime EndUtc,
    Guid? LabId,
    Guid? ProctorUserId,
    string? Instructions,
    bool IsActive);

public record UpdateExamAssignmentsRequest(
    IReadOnlyList<ExamAssignmentRequest> Assignments);

public record LiveRosterItemDto(
    Guid StudentId,
    string Username,
    ExamSessionStatus Status,
    int RemainingSeconds,
    DateTime? LastHeartbeatUtc,
    bool IsOnline);

// Lifecycle requests
public record ArchiveExamRequest(bool IsArchived);
