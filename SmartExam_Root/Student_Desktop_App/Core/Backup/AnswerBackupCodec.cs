using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Student_Desktop_App.Core.Backup;

/// <summary>
/// Module 6 — seals a <see cref="BackupState"/> with AES-256-GCM (in-box <see cref="AesGcm"/>): a fresh
/// 12-byte nonce per write and a 16-byte tag, so any change to the file — one flipped byte, a truncation,
/// a backup copied over from another session — fails authentication instead of silently restoring altered
/// answers (M6-U-03 round-trip, M6-U-04 tamper). The session id is bound in as associated data.
/// Layout: <c>"SXB1" | nonce(12) | tag(16) | ciphertext</c>. Key handling lives in <see cref="AnswerBackupStore"/>.
/// </summary>
public static class AnswerBackupCodec
{
    public const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] Magic = "SXB1"u8.ToArray();
    private const int HeaderSize = 4 + NonceSize + TagSize;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static byte[] Encode(BackupState state, byte[] key)
    {
        RequireKey(key);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        var output = new byte[HeaderSize + plaintext.Length];
        Magic.CopyTo(output, 0);
        var nonce = output.AsSpan(4, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        var tag = output.AsSpan(4 + NonceSize, TagSize);
        var ciphertext = output.AsSpan(HeaderSize);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(state.SessionId));
        CryptographicOperations.ZeroMemory(plaintext);
        return output;
    }

    /// <summary>Opens a backup of <paramref name="expectedSessionId"/>. Throws <see cref="BackupTamperedException"/> on any failure.</summary>
    public static BackupState Decode(ReadOnlySpan<byte> data, byte[] key, Guid expectedSessionId)
    {
        RequireKey(key);
        if (data.Length < HeaderSize || !data[..4].SequenceEqual(Magic))
            throw new BackupTamperedException("The answer backup is not a SmartExam backup or is truncated.");

        var nonce = data.Slice(4, NonceSize);
        var tag = data.Slice(4 + NonceSize, TagSize);
        var ciphertext = data[HeaderSize..];
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData(expectedSessionId));
        }
        catch (CryptographicException ex)
        {
            throw new BackupTamperedException("The answer backup failed its integrity check.", ex);
        }

        try
        {
            var state = JsonSerializer.Deserialize<BackupState>(plaintext, JsonOptions)
                        ?? throw new BackupTamperedException("The answer backup is empty.");
            if (state.SessionId != expectedSessionId)
                throw new BackupTamperedException("The answer backup belongs to another session.");
            return state;
        }
        catch (JsonException ex)
        {
            throw new BackupTamperedException("The answer backup could not be read.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static byte[] NewKey() => RandomNumberGenerator.GetBytes(KeySize);

    private static byte[] AssociatedData(Guid sessionId) =>
        Encoding.ASCII.GetBytes("SmartExam answer backup " + sessionId.ToString("D"));

    private static void RequireKey(byte[] key)
    {
        if (key is null || key.Length != KeySize)
            throw new ArgumentException($"The backup key must be {KeySize} bytes.", nameof(key));
    }
}
