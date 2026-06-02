using System.ComponentModel.DataAnnotations;
using Backend_API.Models;

namespace Backend_API.Contracts;

public record CreateUserRequest(
    [property: Required, MinLength(3), MaxLength(120)] string Username,
    [property: Required, EmailAddress, MaxLength(256)] string Email,
    [property: Required, MinLength(8), MaxLength(128)] string Password);

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
    DateTime CreatedAtUtc);
