using System.ComponentModel.DataAnnotations;

namespace Backend_API.Contracts;

public record DepartmentDto(
    Guid Id,
    string Name,
    string Code,
    bool IsActive,
    int TeacherCount,
    int StudentCount,
    int SectionCount);

public record AcademicSectionDto(
    Guid Id,
    Guid DepartmentId,
    string DepartmentName,
    string Name,
    string Code,
    string AcademicYear,
    string? Semester,
    bool IsActive,
    int StudentCount);

public record CreateDepartmentRequest(
    [Required, MinLength(2), MaxLength(200)] string Name,
    [Required, MinLength(2), MaxLength(40)] string Code);

public record CreateAcademicSectionRequest(
    [Required] Guid DepartmentId,
    [Required, MinLength(2), MaxLength(200)] string Name,
    [Required, MinLength(2), MaxLength(60)] string Code,
    [Required, MinLength(4), MaxLength(40)] string AcademicYear,
    [MaxLength(40)] string? Semester);
