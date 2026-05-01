using System.ComponentModel.DataAnnotations;
using Backend_API.Models;

namespace Backend_API.Contracts;

public record BootstrapAdminRequest(
    [Required, MinLength(2), MaxLength(200)] string InstitutionName,
    [Required, MinLength(3), MaxLength(120)] string Username,
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MinLength(8), MaxLength(128)] string Password);

public record LoginRequest(
    [Required, MaxLength(256)] string UsernameOrEmail,
    [Required, MinLength(8), MaxLength(128)] string Password);

public record StudentLoginRequest(
    [Required, MaxLength(256)] string UsernameOrEmail,
    [Required, MinLength(8), MaxLength(128)] string Password,
    [Required, MinLength(8), MaxLength(512)] string HardwareFingerprint);

public record RefreshTokenRequest([Required] string RefreshToken);

public record UserSummaryDto(
    Guid Id,
    Guid? InstitutionId,
    string Username,
    string Email,
    SystemRole Role);

public record TokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAtUtc,
    UserSummaryDto User,
    bool DeviceBound);