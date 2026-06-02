using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Backend_API.Services;

public class HwidService(IOptions<JwtOptions> jwtOptions) : IHwidService
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    public string HashFingerprint(string hardwareFingerprint)
    {
        var normalized = hardwareFingerprint.Trim().ToUpperInvariant();
        var pepper = string.IsNullOrWhiteSpace(_jwtOptions.HwidPepper) ? "fallback-pepper" : _jwtOptions.HwidPepper;
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes($"{normalized}:{pepper}"));
        return Convert.ToHexString(bytes);
    }
}