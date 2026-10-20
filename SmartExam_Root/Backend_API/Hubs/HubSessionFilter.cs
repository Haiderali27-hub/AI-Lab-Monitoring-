using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Backend_API.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Hubs;

/// <summary>
/// D-41 — re-checks the caller's sign-in on every hub method call, not only when the connection is opened.
/// The JWT is validated once, at connect (Program.cs OnTokenValidated); every hub method then authorizes from the
/// claims of that connection. Without this filter a staff member who was deactivated, force-logged-out or given
/// another role kept the old powers on an already open Live Monitor socket (join any exam group as an ex-admin,
/// unlock / force-submit / extend a session of an exam they used to supervise). A call is refused — and the
/// connection closed — when the token's session (jti) was revoked, the account is gone or inactive, or the role in
/// the token is no longer the account's role.
/// </summary>
public sealed class HubSessionFilter : IHubFilter
{
    public const string SessionEndedMessage = "Your sign-in has ended. Sign in again.";

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var db = invocationContext.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await FailureReasonAsync(db, invocationContext.Context.User, checkRole: true) is not null)
        {
            invocationContext.Context.Abort();
            throw new HubException(SessionEndedMessage);
        }
        return await next(invocationContext);
    }

    /// <summary>
    /// Why the signed-in <paramref name="user"/> may no longer act (null = still valid): its token session (jti)
    /// was revoked, the account is unknown or deactivated, or (with <paramref name="checkRole"/>) the token's role
    /// differs from the account's current role. Shared with the JWT bearer's OnTokenValidated.
    /// </summary>
    public static async Task<string?> FailureReasonAsync(AppDbContext db, ClaimsPrincipal? user, bool checkRole)
    {
        if (user?.Identity?.IsAuthenticated != true) return "Not signed in.";

        var jti = user.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (jti is not null && await db.UserSessions.AsNoTracking().AnyAsync(s => s.Jti == jti && s.IsRevoked))
            return "Session revoked.";

        if (!Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            return "Unknown account.";
        var account = await db.Users.AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => new { u.IsActive, u.Role })
            .FirstOrDefaultAsync();
        if (account is null || !account.IsActive) return "Account inactive.";
        if (checkRole && !string.Equals(user.FindFirst(ClaimTypes.Role)?.Value, account.Role.ToString(), StringComparison.Ordinal))
            return "Role changed.";
        return null;
    }
}
