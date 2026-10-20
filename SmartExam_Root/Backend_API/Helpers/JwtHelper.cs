using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Backend_API.Models;
using Microsoft.IdentityModel.Tokens;

namespace Backend_API.Helpers;

public class JwtHelper
{
    // Module 1: access tokens are short-lived; clients renew them with a rotating refresh token.
    public const double DefaultExpiryMinutes = 30;

    private readonly IConfiguration _config;

    public JwtHelper(IConfiguration config)
    {
        _config = config;
    }

    public double ExpiryMinutes =>
        double.TryParse(_config["Jwt:ExpiryMinutes"], NumberStyles.Float, CultureInfo.InvariantCulture, out var m) && m > 0
            ? m
            : DefaultExpiryMinutes;

    /// <param name="extraClaims">Channel-specific claims, e.g. the desktop client's
    /// <c>client=desktop</c> + <c>machine=&lt;PC name&gt;</c> used for seat enforcement (Module 9).</param>
    public (string token, string jti, DateTime expiry) GenerateToken(User user, IEnumerable<Claim>? extraClaims = null)
    {
        var jti = Guid.NewGuid().ToString();
        // One clock reading for both iat and exp, so exp - iat is exactly the configured lifetime.
        var now = DateTime.UtcNow;
        var expiry = now.AddMinutes(ExpiryMinutes);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, jti),
            new Claim(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(now).ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("name", user.Name),
            new Claim("email", user.Email)
        };
        if (extraClaims is not null) claims.AddRange(extraClaims);

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!)
        );
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            notBefore: now,
            expires: expiry,
            signingCredentials: creds
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), jti, expiry);
    }
}
