using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Student_Desktop_App.Core.Monitoring;

/// <summary>One monitoring event waiting for the server. <see cref="RecordedAtUtc"/> is when it happened on this PC.</summary>
public sealed record QueuedEvent(Guid ClientEventId, string EventType, string PayloadJson, DateTime RecordedAtUtc, bool Urgent = false);

/// <summary>
/// Module 5 — the offline-safe outbox for monitoring events (violations, focus loss, camera findings, process
/// changes). Every event is appended to an encrypted file before anything is sent, so nothing is lost when the
/// network drops or the app is killed; <see cref="EventBatcher"/> decides when to flush and the server's
/// <c>clientEventId</c> dedup makes a resend after a lost reply harmless. Order and original timestamps are kept
/// (M5-U-01), the file is never plaintext (M5-U-02: each record is sealed by <see cref="RecordCipher"/>) and it
/// survives a restart (M5-U-03). File layout: <c>"SXEQ" | version(1)</c> then records
/// <c>length(int32 LE) | sealed JSON</c>. A record that fails authentication is skipped and counted in
/// <see cref="RejectedRecords"/>; a torn record at the end (crash mid-append) is dropped. Thread-safe.
/// </summary>
public sealed class MonitoringEventQueue
{
    private static readonly byte[] Magic = "SXEQ"u8.ToArray();
    private const byte Version = 1;
    private const int MaxRecordBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _path;
    private readonly RecordCipher _cipher;
    private readonly List<QueuedEvent> _items = new();
    private readonly object _gate = new();

    /// <summary>Opens (or creates) the queue file and loads what an earlier run left in it.</summary>
    public MonitoringEventQueue(string path, RecordCipher cipher)
    {
        _path = path;
        _cipher = cipher;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Load();
    }

    /// <summary>Records found on disk that were not authentic (edited or from another session) and were skipped.</summary>
    public int RejectedRecords { get; private set; }

    public int Count
    {
        get { lock (_gate) return _items.Count; }
    }

    /// <summary>True when an urgent (violation) event is waiting.</summary>
    public bool HasUrgent
    {
        get { lock (_gate) return _items.Any(i => i.Urgent); }
    }

    /// <summary>Appends an event durably (written through to disk before this returns).</summary>
    public void Enqueue(QueuedEvent item)
    {
        lock (_gate)
        {
            var record = Seal(item);
            using (var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough))
            {
                if (fs.Length == 0) WriteHeader(fs);
                fs.Write(record);
                fs.Flush(flushToDisk: true);
            }
            _items.Add(item);
        }
    }

    /// <summary>The oldest <paramref name="max"/> events, in the order they were queued.</summary>
    public IReadOnlyList<QueuedEvent> PeekBatch(int max)
    {
        lock (_gate) return _items.Take(Math.Max(0, max)).ToList();
    }

    /// <summary>Removes delivered events and rewrites the file (atomically) with what is left.</summary>
    public void Ack(IEnumerable<Guid> clientEventIds)
    {
        var done = clientEventIds.ToHashSet();
        if (done.Count == 0) return;
        lock (_gate)
        {
            if (_items.RemoveAll(i => done.Contains(i.ClientEventId)) == 0) return;
            Rewrite();
        }
    }

    /// <summary>Deletes the queue file (the session is over and everything was delivered or given up).</summary>
    public void DeleteFile()
    {
        lock (_gate)
        {
            _items.Clear();
            if (File.Exists(_path)) File.Delete(_path);
        }
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        var bytes = File.ReadAllBytes(_path);
        if (bytes.Length == 0) return;
        if (bytes.Length < 5 || !bytes.AsSpan(0, 4).SequenceEqual(Magic) || bytes[4] != Version)
        {
            // Not our file (or overwritten): keep it aside for inspection, start empty.
            RejectedRecords++;
            Quarantine();
            return;
        }

        var offset = 5;
        while (offset + 4 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
            if (length <= 0 || length > MaxRecordBytes || offset + 4 + length > bytes.Length) break; // torn tail
            try
            {
                var plain = _cipher.Open(bytes.AsSpan(offset + 4, length));
                var item = JsonSerializer.Deserialize<QueuedEvent>(plain, Json);
                if (item is not null) _items.Add(item);
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException)
            {
                RejectedRecords++;
            }
            offset += 4 + length;
        }

        // Rewrite when anything was dropped, so the file holds exactly what is queued.
        if (RejectedRecords > 0 || offset != bytes.Length) Rewrite();
    }

    private void Rewrite()
    {
        var tmp = _path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            WriteHeader(fs);
            foreach (var item in _items) fs.Write(Seal(item));
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, _path, overwrite: true);
    }

    private byte[] Seal(QueuedEvent item)
    {
        var sealedRecord = _cipher.Seal(JsonSerializer.SerializeToUtf8Bytes(item, Json));
        var record = new byte[4 + sealedRecord.Length];
        BinaryPrimitives.WriteInt32LittleEndian(record, sealedRecord.Length);
        sealedRecord.CopyTo(record, 4);
        return record;
    }

    private static void WriteHeader(Stream fs)
    {
        fs.Write(Magic);
        fs.WriteByte(Version);
    }

    private void Quarantine()
    {
        try
        {
            File.Move(_path, _path + ".rejected", overwrite: true);
        }
        catch (IOException)
        {
            File.Delete(_path);
        }
    }
}
