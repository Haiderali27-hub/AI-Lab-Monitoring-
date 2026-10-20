using System.Security.Cryptography;
using System.Text;

namespace Backend_API.Helpers;

/// <summary>
/// Hashes the raw hardware fingerprint the client sends before it is stored/compared, so the
/// database never holds raw device identifiers. Deterministic: the same device always yields the
/// same hash, which is what device-binding needs.
/// Fingerprint formats: v1 (legacy) "CPU:…|BOARD:…|MAC:…"; v2 "v2|BIOS serial|disk serial|baseboard serial".
/// </summary>
public static class HwidHelper
{
    public const string V2Prefix = "v2|";

    public static string Hash(string rawFingerprint)
    {
        if (string.IsNullOrWhiteSpace(rawFingerprint)) return string.Empty;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawFingerprint.Trim()));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>2 for a "v2|…" fingerprint, otherwise 1 (legacy or opaque test fingerprints).</summary>
    public static int VersionOf(string? rawFingerprint) =>
        rawFingerprint is not null && rawFingerprint.Trim().StartsWith(V2Prefix, StringComparison.Ordinal) ? 2 : 1;

    /// <summary>
    /// The one-time v1 → v2 upgrade rule: a v1 binding that does not match the presented v2 fingerprint
    /// is upgraded only when the same request also proves the old fingerprint it was bound with.
    /// </summary>
    public static bool CanUpgradeToV2(int bindingVersion, string bindingHash, string presentedFingerprint, string? legacyFingerprint) =>
        bindingVersion < 2 &&
        VersionOf(presentedFingerprint) == 2 &&
        !string.IsNullOrWhiteSpace(legacyFingerprint) &&
        VersionOf(legacyFingerprint) == 1 &&
        FixedTimeEquals(Hash(legacyFingerprint), bindingHash);

    public static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
