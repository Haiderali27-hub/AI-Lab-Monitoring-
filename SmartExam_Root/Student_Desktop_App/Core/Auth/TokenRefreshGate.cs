using Student_Desktop_App.Services;

namespace Student_Desktop_App.Core.Auth;

public enum RefreshStatus
{
    /// A new token pair was obtained (or another caller had just obtained one).
    Refreshed,
    /// The server refused the refresh token (revoked, reused, expired, user deactivated): the session is over.
    Rejected,
    /// The server could not be reached / was busy. The session may still be fine — keep the current token.
    Unavailable
}

public sealed record RefreshAttempt(RefreshStatus Status, TokenResponse? Token = null)
{
    public static readonly RefreshAttempt Rejected = new(RefreshStatus.Rejected);
    public static readonly RefreshAttempt Unavailable = new(RefreshStatus.Unavailable);
}

/// <summary>
/// Pure single-flight coordinator for the access-token refresh (Module 1), so it is unit-testable.
/// The server rotates refresh tokens and treats a replayed (already rotated) one as theft, revoking the
/// whole family. Two concurrent 401s (e.g. heartbeat + answer save) must therefore never both send the
/// same refresh token: callers queue on a semaphore, and a caller whose failed token has already been
/// replaced by someone else simply reuses the new token.
/// </summary>
public sealed class TokenRefreshGate
{
    /// Refresh this long before the access token expires, so in-flight requests never race the expiry.
    public static readonly TimeSpan ProactiveWindow = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<TokenResponse?> _current;
    private readonly Action<TokenResponse> _store;
    private readonly Func<string, CancellationToken, Task<RefreshAttempt>> _refresh;
    private readonly Func<DateTime> _utcNow;

    /// <param name="current">Reads the current session token (null = logged out).</param>
    /// <param name="store">Persists a newly issued token pair.</param>
    /// <param name="refresh">Calls POST /api/auth/refresh with the given refresh token.</param>
    public TokenRefreshGate(Func<TokenResponse?> current, Action<TokenResponse> store,
        Func<string, CancellationToken, Task<RefreshAttempt>> refresh, Func<DateTime>? utcNow = null)
    {
        _current = current;
        _store = store;
        _refresh = refresh;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public bool IsNearExpiry(TokenResponse token) => _utcNow() >= token.AccessTokenExpiresAtUtc - ProactiveWindow;

    /// <summary>
    /// The access token to put on the next request: the current one, refreshed first when it is about to
    /// expire. Returns null when logged out. If the proactive refresh cannot reach the server, the current
    /// token is returned (it may still be valid; a 401 will retry the refresh).
    /// </summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = _current();
        if (token is null) return null;
        if (!IsNearExpiry(token) || string.IsNullOrWhiteSpace(token.RefreshToken)) return token.AccessToken;

        var attempt = await RefreshAsync(token.AccessToken, cancellationToken);
        return attempt.Token?.AccessToken ?? _current()?.AccessToken;
    }

    /// <summary>
    /// Refresh after <paramref name="failedAccessToken"/> was rejected (or is about to expire).
    /// Single-flight: at most one refresh request is in flight, and a token that another caller already
    /// replaced is not refreshed again.
    /// </summary>
    public async Task<RefreshAttempt> RefreshAsync(string failedAccessToken, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var token = _current();
            if (token is null) return RefreshAttempt.Rejected;

            // Someone else refreshed while we waited: use their result instead of replaying.
            if (!string.Equals(token.AccessToken, failedAccessToken, StringComparison.Ordinal) && !IsNearExpiry(token))
                return new RefreshAttempt(RefreshStatus.Refreshed, token);

            if (string.IsNullOrWhiteSpace(token.RefreshToken)) return RefreshAttempt.Rejected;

            var attempt = await _refresh(token.RefreshToken, cancellationToken);
            if (attempt.Status == RefreshStatus.Refreshed && attempt.Token is not null)
            {
                // A logout during the call wins: don't resurrect a cleared session.
                if (_current() is null) return RefreshAttempt.Rejected;
                _store(attempt.Token);
                return attempt;
            }

            return attempt.Status == RefreshStatus.Refreshed ? RefreshAttempt.Unavailable : attempt;
        }
        finally
        {
            _gate.Release();
        }
    }
}
