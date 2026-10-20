using Backend_API.Models.Enums;

namespace Backend_API.DTOs.Auth;

public record LoginResponse(
    string Token,
    Guid UserId,
    string Name,
    string Email,
    UserRole Role,
    bool DeviceBound, // true if HWID was registered or already matched
    // Module 1: rotating refresh token for POST /api/auth/refresh (appended; older clients read only Token)
    string? RefreshToken = null,
    DateTime? AccessTokenExpiresAtUtc = null
);
