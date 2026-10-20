namespace Student_Desktop_App.Services;

/// <summary>
/// The signed-in student's token pair. <see cref="ApiClient"/> reads it at send time and replaces it
/// after every refresh (the server rotates the refresh token on each use).
/// </summary>
public static class SessionStore
{
    private static volatile TokenResponse? _current;

    public static TokenResponse? CurrentToken => _current;

    public static bool IsAuthenticated => _current is not null;

    public static bool IsAccessTokenExpired
    {
        get
        {
            var token = _current;
            return token is null || DateTime.UtcNow >= token.AccessTokenExpiresAtUtc.AddSeconds(-15);
        }
    }

    public static void Set(TokenResponse tokenResponse) => _current = tokenResponse;

    /// Replaces the whole pair: a refresh returns a new access token AND a new refresh token.
    public static void UpdateAccessToken(TokenResponse tokenResponse) => _current = tokenResponse;

    public static void Clear() => _current = null;
}
