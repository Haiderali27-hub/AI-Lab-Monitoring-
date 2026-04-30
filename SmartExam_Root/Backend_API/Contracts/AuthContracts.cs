using System.ComponentModel.DataAnnotations;
using Backend_API.Models;

namespace Backend_API.Contracts;

public record BootstrapAdminRequest(
    [property: Required, MinLength(2), MaxLength(200)] string InstitutionName,
    [property: Required, MinLength(3), MaxLength(120)] string Username,
    [property: Required, EmailAddress, MaxLength(256)] string Email,
    [property: Required, MinLength(8), MaxLength(128)] string Password);

public record LoginRequest(
    [property: Required, MaxLength(256)] string UsernameOrEmail,
    [property: Required, MinLength(8), MaxLength(128)] string Password);

public record StudentLoginRequest(
    [property: Required, MaxLength(256)] string UsernameOrEmail,
    [property: Required, MinLength(8), MaxLength(128)] string Password,
    [property: Required, MinLength(8), MaxLength(512)] string HardwareFingerprint);

public record RefreshTokenRequest([property: Required] string RefreshToken);

public record UserSummaryDto(
    Guid Id,
    Guid InstitutionId,
    string Username,
    string Email,
    SystemRole Role);

public record TokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAtUtc,
    UserSummaryDto User,
    bool DeviceBound);