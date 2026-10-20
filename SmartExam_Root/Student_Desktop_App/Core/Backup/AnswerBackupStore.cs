using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;

namespace Student_Desktop_App.Core.Backup;

/// <summary>Wraps (encrypts) a backup's data key. Production: DPAPI CurrentUser (<c>Services/Backup/DpapiKeyProtector</c>).</summary>
public interface IKeyProtector
{
    byte[] Protect(byte[] key, byte[] entropy);

    /// <summary>Throws <see cref="CryptographicException"/> when the blob was altered or belongs to another Windows account.</summary>
    byte[] Unprotect(byte[] wrappedKey, byte[] entropy);
}

public enum BackupLoadStatus { Missing, Ok, Tampered }

public sealed record BackupLoadResult(BackupLoadStatus Status, BackupState? State = null, string? Error = null);

/// <summary>
/// Module 6 — the encrypted local answer backup, one file per exam session:
/// <c>%LOCALAPPDATA%\SmartExam\backup\{sessionId}.seb</c>. Each session gets a random AES-256 data key,
/// stored in the file header wrapped by <see cref="IKeyProtector"/> (DPAPI, current Windows user, the session
/// id as entropy), so the file can only be opened by the same Windows account and any edit is detected by
/// the AES-GCM tag (<see cref="AnswerBackupCodec"/>). Writes are atomic (<c>.tmp</c> then replace), so a crash
/// mid-write leaves the previous backup intact. A DPAPI key is tied to the Windows profile: a backup cannot be
/// restored on another PC — by design; the server copy is the cross-machine source of truth.
/// File layout: <c>"SXBK" | version(1) | wrappedKeyLength(int32 LE) | wrappedKey | codec payload</c>.
/// </summary>
public sealed class AnswerBackupStore
{
    public const string Extension = ".seb";
    private static readonly byte[] Magic = "SXBK"u8.ToArray();
    private const byte Version = 1;
    private const int MaxWrappedKeyLength = 4096;

    private readonly IKeyProtector _protector;
    private readonly Func<DateTime> _utcClock;
    private readonly Dictionary<Guid, (byte[] Key, byte[] Wrapped)> _keys = new();
    private readonly object _gate = new();

    public AnswerBackupStore(string directory, IKeyProtector protector, Func<DateTime>? utcClock = null)
    {
        Directory = directory;
        _protector = protector;
        _utcClock = utcClock ?? (() => DateTime.UtcNow);
    }

    public string Directory { get; }

    /// <summary>Default location: <c>%LOCALAPPDATA%\SmartExam\backup</c>.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartExam", "backup");

    public string PathFor(Guid sessionId) => Path.Combine(Directory, sessionId.ToString("D") + Extension);

    /// <summary>Writes the backup atomically (stamping <see cref="BackupState.SavedAtUtc"/>).</summary>
    public void Save(BackupState state)
    {
        lock (_gate)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var (key, wrapped) = KeyFor(state.SessionId);
            var payload = AnswerBackupCodec.Encode(state with { SavedAtUtc = _utcClock() }, key);

            var file = new byte[Magic.Length + 1 + 4 + wrapped.Length + payload.Length];
            Magic.CopyTo(file, 0);
            file[4] = Version;
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(5, 4), wrapped.Length);
            wrapped.CopyTo(file, 9);
            payload.CopyTo(file, 9 + wrapped.Length);

            var path = PathFor(state.SessionId);
            var tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(file);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
    }

    /// <summary>Reads a session's backup. A file that fails any check is reported as <see cref="BackupLoadStatus.Tampered"/>.</summary>
    public BackupLoadResult Load(Guid sessionId)
    {
        lock (_gate)
        {
            var path = PathFor(sessionId);
            if (!File.Exists(path)) return new BackupLoadResult(BackupLoadStatus.Missing);

            try
            {
                var bytes = File.ReadAllBytes(path);
                var (key, wrapped, payloadOffset) = OpenHeader(bytes, sessionId);
                var state = AnswerBackupCodec.Decode(bytes.AsSpan(payloadOffset), key, sessionId);
                _keys[sessionId] = (key, wrapped);
                return new BackupLoadResult(BackupLoadStatus.Ok, state);
            }
            catch (BackupTamperedException ex)
            {
                return new BackupLoadResult(BackupLoadStatus.Tampered, null, ex.Message);
            }
            catch (IOException ex)
            {
                return new BackupLoadResult(BackupLoadStatus.Tampered, null, "The answer backup could not be read: " + ex.Message);
            }
        }
    }

    /// <summary>Records the receipt and marks the backup as submitted (it is then only kept as evidence).</summary>
    public void MarkSubmitted(Guid sessionId, BackupReceipt? receipt)
    {
        lock (_gate)
        {
            var loaded = Load(sessionId);
            if (loaded.State is null) return;
            Save(loaded.State with { Submitted = true, Receipt = receipt ?? loaded.State.Receipt });
        }
    }

    /// <summary>Unsubmitted backups of <paramref name="userId"/> (crash recovery: "your answers are on this PC").</summary>
    public IReadOnlyList<BackupState> FindResumable(Guid userId)
    {
        var found = new List<BackupState>();
        foreach (var (sessionId, _) in EnumerateBackups())
        {
            var result = Load(sessionId);
            if (result.State is { Submitted: false } state && state.UserId == userId) found.Add(state);
        }
        return found.OrderByDescending(s => s.SavedAtUtc).ToList();
    }

    /// <summary>
    /// Startup housekeeping: deletes submitted (and unreadable) backups older than <paramref name="submittedMaxAge"/>,
    /// unsubmitted ones older than <paramref name="abandonedMaxAge"/> (the exam is long over; the server copy
    /// stands) and stray <c>.tmp</c> files. Returns how many backups were deleted.
    /// </summary>
    public int Cleanup(TimeSpan submittedMaxAge, TimeSpan abandonedMaxAge)
    {
        if (!System.IO.Directory.Exists(Directory)) return 0;
        var now = _utcClock();
        var deleted = 0;

        foreach (var tmp in System.IO.Directory.EnumerateFiles(Directory, "*" + Extension + ".tmp"))
            TryDelete(tmp);

        foreach (var (sessionId, path) in EnumerateBackups())
        {
            var age = now - File.GetLastWriteTimeUtc(path);
            if (age < submittedMaxAge) continue;

            var result = Load(sessionId);
            var expired = result.Status != BackupLoadStatus.Ok || result.State!.Submitted || age >= abandonedMaxAge;
            if (expired && TryDelete(path))
            {
                deleted++;
                lock (_gate) _keys.Remove(sessionId);
            }
        }
        return deleted;
    }

    private IEnumerable<(Guid SessionId, string Path)> EnumerateBackups()
    {
        if (!System.IO.Directory.Exists(Directory)) yield break;
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*" + Extension))
        {
            if (Guid.TryParse(Path.GetFileNameWithoutExtension(path), out var sessionId))
                yield return (sessionId, path);
        }
    }

    private (byte[] Key, byte[] Wrapped) KeyFor(Guid sessionId)
    {
        if (_keys.TryGetValue(sessionId, out var cached)) return cached;

        // Reuse the key of an existing, intact backup so a restarted app keeps writing the same file format;
        // otherwise (first write, or a file that failed its checks) start with a fresh key.
        var path = PathFor(sessionId);
        if (File.Exists(path))
        {
            try
            {
                var (key, wrapped, _) = OpenHeader(File.ReadAllBytes(path), sessionId);
                return _keys[sessionId] = (key, wrapped);
            }
            catch (Exception ex) when (ex is BackupTamperedException or IOException)
            {
                // fall through to a new key — the caller already reported the tampered file on load
            }
        }

        var fresh = AnswerBackupCodec.NewKey();
        return _keys[sessionId] = (fresh, _protector.Protect(fresh, Entropy(sessionId)));
    }

    private (byte[] Key, byte[] Wrapped, int PayloadOffset) OpenHeader(byte[] bytes, Guid sessionId)
    {
        if (bytes.Length < 9 || !bytes.AsSpan(0, 4).SequenceEqual(Magic) || bytes[4] != Version)
            throw new BackupTamperedException("The answer backup header is invalid.");
        var wrappedLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(5, 4));
        if (wrappedLength <= 0 || wrappedLength > MaxWrappedKeyLength || 9 + wrappedLength > bytes.Length)
            throw new BackupTamperedException("The answer backup header is invalid.");

        var wrapped = bytes.AsSpan(9, wrappedLength).ToArray();
        byte[] key;
        try
        {
            key = _protector.Unprotect(wrapped, Entropy(sessionId));
        }
        catch (CryptographicException ex)
        {
            throw new BackupTamperedException("The answer backup key cannot be opened on this Windows account.", ex);
        }
        if (key.Length != AnswerBackupCodec.KeySize)
            throw new BackupTamperedException("The answer backup key is invalid.");
        return (key, wrapped, 9 + wrappedLength);
    }

    private static byte[] Entropy(Guid sessionId) => sessionId.ToByteArray();

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
