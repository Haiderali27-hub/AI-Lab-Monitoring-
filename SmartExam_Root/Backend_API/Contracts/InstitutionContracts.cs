using System.ComponentModel.DataAnnotations;

namespace Backend_API.Contracts;

public record InstitutionDetailDto(
    Guid Id,
    string Name,
    string ContactEmail,
    string? LogoUrl,
    string? AllowedIpRanges,
    bool EnforceSingleDeviceBinding,
    bool AllowTeacherResetBinding,
    int SessionTimeoutMinutes,
    DateTime CreatedAtUtc);

public record UpdateInstitutionRequest(
    [Required, MinLength(2), MaxLength(200)] string Name,
    [Required, EmailAddress, MaxLength(256)] string ContactEmail,
    [MaxLength(1024)] string? LogoUrl,
    [MaxLength(2000)] string? AllowedIpRanges,
    bool EnforceSingleDeviceBinding,
    bool AllowTeacherResetBinding,
    [Range(5, 1440)] int SessionTimeoutMinutes);

public record LabDto(
    Guid Id,
    string Name,
    int RegisteredTerminals,
    bool IsActive);

public record CreateLabRequest(
    [Required, MinLength(2), MaxLength(200)] string Name,
    [Range(0, 1000)] int RegisteredTerminals);

public record WorkstationDto(
    Guid Id,
    Guid LabId,
    string Name,
    string? IpAddress,
    bool IsActive);

public record CreateWorkstationRequest(
    [Required, MinLength(1), MaxLength(100)] string Name,
    [MaxLength(50)] string? IpAddress);

public record UpdateWorkstationRequest(
    [Required, MinLength(1), MaxLength(100)] string Name,
    [MaxLength(50)] string? IpAddress,
    bool IsActive);
