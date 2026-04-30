using Backend_API.Models;

namespace Backend_API.Services;

public interface IJwtTokenService
{
    string GenerateAccessToken(User user, Guid sessionId, out DateTime expiresAtUtc, out string jti);
    string GenerateRefreshToken();
    string HashToken(string token);
}