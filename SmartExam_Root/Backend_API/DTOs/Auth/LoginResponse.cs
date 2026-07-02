using Backend_API.Models.Enums;

namespace Backend_API.DTOs.Auth;

public record LoginResponse(
    string Token,
    Guid UserId,
    string Name,
    string Email,
    UserRole Role,
    bool DeviceBound  // true if HWID was registered or already matched
);
