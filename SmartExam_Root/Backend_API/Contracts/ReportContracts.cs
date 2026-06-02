namespace Backend_API.Contracts;

public record ExamReportSummaryDto(
    Guid ExamId,
    string ExamName,
    DateTime StartUtc,
    DateTime EndUtc,
    string Status,
    int CandidateCount,
    int AttendanceCount,
    int IdentityVerifiedCount,
    int IncidentCount);

public record ExamReportStudentDto(
    Guid StudentId,
    string Username,
    string Email,
    bool HasBinding,
    DateTime? AttendanceAtUtc,
    DateTime? IdentityVerifiedAtUtc,
    DateTime? LastHeartbeatUtc);

public record ExamReportDetailDto(
    Guid ExamId,
    string ExamName,
    DateTime StartUtc,
    DateTime EndUtc,
    string Status,
    int CandidateCount,
    int AttendanceCount,
    int IdentityVerifiedCount,
    int IncidentCount,
    IReadOnlyList<ExamReportStudentDto> Students);
