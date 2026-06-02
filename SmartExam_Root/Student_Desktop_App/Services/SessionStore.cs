namespace Student_Desktop_App.Services;

public static class SessionStore
{
    public static TokenResponse? CurrentToken { get; private set; }

    public static bool IsAuthenticated => CurrentToken is not null;

    public static bool IsAccessTokenExpired
    {
        get
        {
            if (CurrentToken is null)
            {
                return true;
            }

            return DateTime.UtcNow >= CurrentToken.AccessTokenExpiresAtUtc.AddSeconds(-15);
        }
    }

    public static void Set(TokenResponse tokenResponse)
    {
        CurrentToken = tokenResponse;
    }

    public static void UpdateAccessToken(TokenResponse tokenResponse)
    {
        if (CurrentToken is null)
        {
            CurrentToken = tokenResponse;
            return;
        }

        CurrentToken = tokenResponse;
    }

    public static void Clear()
    {
        CurrentToken = null;
    }
}