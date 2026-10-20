using System.Security.Cryptography;
using System.Text;

namespace Backend_API.Helpers;

public static class PasswordHelper
{
    // Hashes are stored as "pbkdf2$<iterations>$<base64 hash>"; the salt stays in User.Salt.
    // Hashes without the prefix are legacy single-round SHA-256 and are upgraded on next login.
    private const string Prefix = "pbkdf2$";
    private const int Iterations = 210_000;
    private const int HashBytes = 32;

    public static string GenerateSalt()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    public static string HashPassword(string password, string salt)
    {
        var hash = Pbkdf2(password, salt, Iterations);
        return $"{Prefix}{Iterations}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string salt, string hash)
    {
        if (string.IsNullOrEmpty(hash)) return false;

        if (hash.StartsWith(Prefix, StringComparison.Ordinal))
        {
            var parts = hash.Split('$');
            if (parts.Length != 3 || !int.TryParse(parts[1], out var iterations)) return false;

            byte[] expected;
            try { expected = Convert.FromBase64String(parts[2]); }
            catch (FormatException) { return false; }

            return CryptographicOperations.FixedTimeEquals(Pbkdf2(password, salt, iterations), expected);
        }

        var legacy = SHA256.HashData(Encoding.UTF8.GetBytes(password + salt));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Convert.ToBase64String(legacy)),
            Encoding.ASCII.GetBytes(hash));
    }

    /// True when the stored hash should be replaced (legacy SHA-256 or an older iteration count).
    public static bool NeedsRehash(string hash) =>
        !hash.StartsWith($"{Prefix}{Iterations}$", StringComparison.Ordinal);

    private static byte[] Pbkdf2(string password, string salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(salt),
            iterations, HashAlgorithmName.SHA256, HashBytes);
}
