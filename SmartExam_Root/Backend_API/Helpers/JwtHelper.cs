using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Backend_API.Models;
using Microsoft.IdentityModel.Tokens;

namespace Backend_API.Helpers;

public class JwtHelper
{
    private readonly IConfiguration _config;

    public JwtHelper(IConfiguration config)
    {
        _config = config;
    }

    /// <param name="extraClaims">Channel-specific claims, e.g. the desktop client's
    /// <c>client=desktop</c> + <c>machine=&lt;PC name&gt;</c> used for seat enforcement (Module 9).</param>
    public (string token, string jti, DateTime expiry) GenerateToken(User user, IEnumerable<Claim>? extraClaims = null)
    {
        var jti = Guid.NewGuid().ToString();
        var expiry = DateTime.UtcNow.AddMinutes(
            double.Parse(_config["Jwt:ExpiryMinutes"] ?? "480")
        );

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, jti),
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
            expires: expiry,
            signingCredentials: creds
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), jti, expiry);
    }
}
