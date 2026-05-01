using System.ComponentModel.DataAnnotations;
using Backend_API.Models;

namespace Backend_API.Contracts;

public record CreateUserRequest(
    [Required, MinLength(3), MaxLength(120)] string Username,
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MinLength(8), MaxLength(128)] string Password,
    Guid? DepartmentId = null,
    Guid? SectionId = null);

public record StudentBindingStatusDto(
    Guid StudentId,
    string Username,
    string Email,
    bool HasBinding,
    DateTime? BoundAtUtc,
    DateTime? LastSeenAtUtc);

public record BatchUploadResultDto(
    int CreatedCount,
    int SkippedCount,
    IReadOnlyList<string> Errors);

public record UserListDto(
    Guid Id,
    string Username,
    string Email,
    SystemRole Role,
    bool IsActive,
    DateTime CreatedAtUtc,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? SectionId,
    string? SectionName);

public record UpdateUserRequest(
    [Required, MinLength(3), MaxLength(120)] string Username,
    [Required, EmailAddress, MaxLength(256)] string Email,
    bool IsActive);

