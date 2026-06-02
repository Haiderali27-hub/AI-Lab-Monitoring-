namespace Backend_API.Services;

public interface ISessionValidator
{
    Task<bool> ValidateAsync(Guid sessionId, string jti, Guid userId, CancellationToken cancellationToken);
}