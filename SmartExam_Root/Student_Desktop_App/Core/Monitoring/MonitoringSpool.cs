using System.IO;
using System.Security.Cryptography;
using Student_Desktop_App.Core.Backup;

namespace Student_Desktop_App.Core.Monitoring;

/// <summary>
/// Module 5 — one exam session's offline-safe monitoring storage on this PC:
/// <c>%LOCALAPPDATA%\SmartExam\monitoring\{sessionId}\</c> holding <c>key.bin</c> (the AES key, wrapped by DPAPI),
/// <c>events.q</c> (<see cref="MonitoringEventQueue"/>) and <c>captures\</c> (<see cref="CaptureSpool"/>).
/// It survives a crash or a relaunch (a resumed session continues where it left off) and is deleted once
/// everything was delivered. A key that no longer opens (edited, or another Windows account) means the spool
/// cannot be trusted: it is wiped, started afresh and <see cref="WasReset"/> tells the caller to report it.
/// </summary>
public sealed class MonitoringSpool
{
    private MonitoringSpool(string directory, MonitoringEventQueue events, CaptureSpool captures, bool wasReset)
    {
        Directory = directory;
        Events = events;
        Captures = captures;
        WasReset = wasReset;
    }

    public string Directory { get; }
    public MonitoringEventQueue Events { get; }
    public CaptureSpool Captures { get; }

    /// <summary>The previous spool was unreadable or not authentic and was discarded.</summary>
    public bool WasReset { get; }

    /// <summary>Nothing left to deliver.</summary>
    public bool IsEmpty => Events.Count == 0 && Captures.Count == 0;

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartExam", "monitoring");

    public static MonitoringSpool Open(string root, Guid sessionId, IKeyProtector protector)
    {
        var directory = Path.Combine(root, sessionId.ToString("D"));
        var keyPath = Path.Combine(directory, "key.bin");
        var reset = false;
        byte[] key;
        try
        {
            key = SessionKeyFile.LoadOrCreate(keyPath, sessionId, protector);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException)
        {
            Wipe(directory);
            reset = true;
            key = SessionKeyFile.LoadOrCreate(keyPath, sessionId, protector);
        }

        var cipher = new RecordCipher(key, sessionId);
        var events = new MonitoringEventQueue(Path.Combine(directory, "events.q"), cipher);
        var captures = new CaptureSpool(Path.Combine(directory, "captures"), cipher);
        return new MonitoringSpool(directory, events, captures, reset || events.RejectedRecords > 0);
    }

    /// <summary>Removes this session's spool (call only when <see cref="IsEmpty"/>, or when giving up).</summary>
    public void Delete() => Wipe(Directory);

    /// <summary>App-start housekeeping: spools untouched for <paramref name="maxAge"/> belong to long-finished exams.</summary>
    public static int Cleanup(string root, TimeSpan maxAge, DateTime utcNow)
    {
        if (!System.IO.Directory.Exists(root)) return 0;
        var deleted = 0;
        foreach (var dir in System.IO.Directory.EnumerateDirectories(root))
        {
            if (!Guid.TryParse(Path.GetFileName(dir), out _)) continue;
            var newest = System.IO.Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(System.IO.Directory.GetLastWriteTimeUtc(dir))
                .Max();
            if (utcNow - newest < maxAge) continue;
            if (Wipe(dir)) deleted++;
        }
        return deleted;
    }

    private static bool Wipe(string directory)
    {
        try
        {
            if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
