using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using Student_Desktop_App.Core.Backup;

namespace Student_Desktop_App.Core.Monitoring;

/// <summary>
/// Module 5 — seals one record of the on-disk monitoring spool (queued events, pending captures) with AES-256-GCM:
/// a fresh 12-byte nonce per record and a 16-byte tag, the session id bound in as associated data. A record that
/// was edited, truncated or copied from another session fails <see cref="Open"/> with a
/// <see cref="CryptographicException"/> instead of being replayed. Layout: <c>nonce(12) | tag(16) | ciphertext</c>.
/// </summary>
public sealed class RecordCipher
{
    public const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;
    private readonly byte[] _associatedData;

    public RecordCipher(byte[] key, Guid sessionId)
    {
        if (key.Length != KeySize) throw new ArgumentException($"Key must be {KeySize} bytes.", nameof(key));
        _key = key;
        _associatedData = sessionId.ToByteArray();
    }

    public byte[] Seal(ReadOnlySpan<byte> plaintext)
    {
        var output = new byte[NonceSize + TagSize + plaintext.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize), _associatedData);
        return output;
    }

    /// <exception cref="CryptographicException">The record is not authentic for this key and session.</exception>
    public byte[] Open(ReadOnlySpan<byte> sealedRecord)
    {
        if (sealedRecord.Length < NonceSize + TagSize) throw new CryptographicException("Record too short.");
        var plaintext = new byte[sealedRecord.Length - NonceSize - TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(sealedRecord[..NonceSize], sealedRecord[(NonceSize + TagSize)..], sealedRecord.Slice(NonceSize, TagSize), plaintext, _associatedData);
        return plaintext;
    }
}

/// <summary>
/// Module 5 — the per-session data key of the monitoring spool, stored next to it wrapped by an
/// <see cref="IKeyProtector"/> (production: DPAPI CurrentUser, the session id as entropy — the same scheme as the
/// Module 6 answer backup). Layout: <c>"SXMK" | version(1) | wrappedLength(int32 LE) | wrapped</c>.
/// </summary>
public static class SessionKeyFile
{
    private static readonly byte[] Magic = "SXMK"u8.ToArray();
    private const byte Version = 1;

    /// <summary>
    /// Reads the key at <paramref name="path"/>, or creates (and durably writes) a new one when there is none.
    /// </summary>
    /// <exception cref="CryptographicException">The key file exists but was altered or belongs to another Windows account.</exception>
    public static byte[] LoadOrCreate(string path, Guid sessionId, IKeyProtector protector)
    {
        var entropy = sessionId.ToByteArray();
        if (File.Exists(path))
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 9 || !bytes.AsSpan(0, 4).SequenceEqual(Magic) || bytes[4] != Version)
                throw new CryptographicException("Monitoring key file is not in the expected format.");
            var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(5, 4));
            if (length <= 0 || length != bytes.Length - 9)
                throw new CryptographicException("Monitoring key file is truncated.");
            var key = protector.Unprotect(bytes[9..], entropy);
            if (key.Length != RecordCipher.KeySize) throw new CryptographicException("Monitoring key has the wrong size.");
            return key;
        }

        var fresh = RandomNumberGenerator.GetBytes(RecordCipher.KeySize);
        var wrapped = protector.Protect(fresh, entropy);
        var file = new byte[9 + wrapped.Length];
        Magic.CopyTo(file, 0);
        file[4] = Version;
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(5, 4), wrapped.Length);
        wrapped.CopyTo(file, 9);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            fs.Write(file);
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
        return fresh;
    }
}
