using Backend_API.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Services;

public class SessionValidator(AppDbContext dbContext, ILogger<SessionValidator> logger) : ISessionValidator
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly ILogger<SessionValidator> _logger = logger;

    public async Task<bool> ValidateAsync(Guid sessionId, string jti, Guid userId, CancellationToken cancellationToken)
    {
        var session = await _dbContext.UserSessions
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);

        if (session is null)
        {
            _logger.LogWarning("Session {SessionId} not found", sessionId);
            return false;
        }

        if (session.UserId != userId)
        {
            _logger.LogWarning("Session {SessionId} user mismatch", sessionId);
            return false;
        }

        if (session.RevokedAtUtc is not null)
        {
            _logger.LogWarning("Session {SessionId} revoked", sessionId);
            return false;
        }

        if (session.ExpiresAtUtc <= DateTime.UtcNow)
        {
            _logger.LogWarning("Session {SessionId} expired", sessionId);
            return false;
        }

        if (!string.Equals(session.AccessTokenJti, jti, StringComparison.Ordinal))
        {
            _logger.LogWarning("Session {SessionId} token jti mismatch", sessionId);
            return false;
        }

        if (!session.User.IsActive)
        {
            _logger.LogWarning("User {UserId} inactive", userId);
            return false;
        }

        return true;
    }
}