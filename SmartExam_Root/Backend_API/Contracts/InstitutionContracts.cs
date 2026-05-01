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
    [property: Required, MinLength(2), MaxLength(200)] string Name,
    [property: Required, EmailAddress, MaxLength(256)] string ContactEmail,
    [property: MaxLength(1024)] string? LogoUrl,
    [property: MaxLength(2000)] string? AllowedIpRanges,
    bool EnforceSingleDeviceBinding,
    bool AllowTeacherResetBinding,
    [property: Range(5, 1440)] int SessionTimeoutMinutes);

public record LabDto(
    Guid Id,
    string Name,
    int RegisteredTerminals,
    bool IsActive);

public record CreateLabRequest(
    [property: Required, MinLength(2), MaxLength(200)] string Name,
    [property: Range(0, 1000)] int RegisteredTerminals);

public record WorkstationDto(
    Guid Id,
    Guid LabId,
    string Name,
    string? IpAddress,
    bool IsActive);

public record CreateWorkstationRequest(
    [property: Required, MinLength(1), MaxLength(100)] string Name,
    [property: MaxLength(50)] string? IpAddress);

public record UpdateWorkstationRequest(
    [property: Required, MinLength(1), MaxLength(100)] string Name,
    [property: MaxLength(50)] string? IpAddress,
    bool IsActive);
