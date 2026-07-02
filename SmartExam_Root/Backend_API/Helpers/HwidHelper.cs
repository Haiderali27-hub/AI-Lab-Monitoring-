using System.Security.Cryptography;
using System.Text;

namespace Backend_API.Helpers;

/// <summary>
/// Hashes the raw hardware fingerprint (CPU|BOARD|MAC) the client sends before it is
/// stored/compared, so the database never holds raw device identifiers. Deterministic:
/// the same device always yields the same hash, which is what device-binding needs.
/// </summary>
public static class HwidHelper
{
    public static string Hash(string rawFingerprint)
    {
        if (string.IsNullOrWhiteSpace(rawFingerprint)) return string.Empty;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawFingerprint.Trim()));
        return Convert.ToBase64String(bytes);
    }
}
