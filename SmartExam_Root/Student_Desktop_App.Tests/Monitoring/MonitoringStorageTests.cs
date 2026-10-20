using System.IO;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Student_Desktop_App.Core.Backup;
using Student_Desktop_App.Core.Monitoring;
using Student_Desktop_App.Services.Backup;

namespace Student_Desktop_App.Tests.Monitoring;

/// <summary>Stand-in for DPAPI: XOR with the entropy plus a checksum; throws on any mismatch (like a foreign account).</summary>
internal sealed class FakeKeyProtector : IKeyProtector
{
    public byte[] Protect(byte[] key, byte[] entropy)
    {
        var body = key.Select((b, i) => (byte)(b ^ entropy[i % entropy.Length])).ToArray();
        return body.Concat(SHA256.HashData(body.Concat(entropy).ToArray()).Take(8)).ToArray();
    }

    public byte[] Unprotect(byte[] wrappedKey, byte[] entropy)
    {
        if (wrappedKey.Length < 9) throw new CryptographicException("short");
        var body = wrappedKey[..^8];
        if (!SHA256.HashData(body.Concat(entropy).ToArray()).Take(8).SequenceEqual(wrappedKey[^8..]))
            throw new CryptographicException("bad blob");
        return body.Select((b, i) => (byte)(b ^ entropy[i % entropy.Length])).ToArray();
    }
}

/// <summary>
/// M5-U-01…03 — the encrypted offline event outbox — plus the capture spool and the per-session spool folder.
/// Temp directories and a fake key protector only (one test uses the real DPAPI wrapper: harmless, CurrentUser).
/// </summary>
public sealed class MonitoringStorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smartexam-monitoring-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _session = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private RecordCipher Cipher(Guid? session = null) =>
        new(SessionKeyFile.LoadOrCreate(Path.Combine(_dir, "key.bin"), session ?? _session, new FakeKeyProtector()), session ?? _session);

    private string QueuePath => Path.Combine(_dir, "events.q");

    private static QueuedEvent Event(int i, bool urgent = false) =>
        new(Guid.NewGuid(), i % 2 == 0 ? "Violation" : "ProcessList", $"{{\"reason\":\"secret-marker-{i}\"}}", T0.AddSeconds(i), urgent);

    [Fact]
    public void M5_U_01_Queue_ReplaysInOrder_WithOriginalTimestamps()
    {
        var queue = new MonitoringEventQueue(QueuePath, Cipher());
        var events = Enumerable.Range(0, 5).Select(i => Event(i)).ToList();
        foreach (var e in events) queue.Enqueue(e);

        var replay = new MonitoringEventQueue(QueuePath, Cipher()).PeekBatch(10);
        replay.Should().Equal(events);
        replay.Select(e => e.RecordedAtUtc).Should().BeInAscendingOrder().And.Equal(events.Select(e => e.RecordedAtUtc));
        queue.PeekBatch(2).Should().Equal(events.Take(2));
    }

    [Fact]
    public void M5_U_02_QueueFile_IsNotPlaintext()
    {
        var queue = new MonitoringEventQueue(QueuePath, Cipher());
        queue.Enqueue(Event(0));
        queue.Enqueue(Event(1));

        var bytes = File.ReadAllBytes(QueuePath);
        var asText = Encoding.UTF8.GetString(bytes);
        asText.Should().NotContain("secret-marker").And.NotContain("Violation").And.NotContain("clientEventId");
        asText.Should().StartWith("SXEQ");
        File.ReadAllBytes(Path.Combine(_dir, "key.bin")).Length.Should().BeGreaterThan(9, "the key is stored wrapped, next to the queue");
    }

    [Fact]
    public void M5_U_03_Queue_SurvivesRestart_AndAcksAreDurable()
    {
        var events = Enumerable.Range(0, 4).Select(i => Event(i)).ToList();
        var first = new MonitoringEventQueue(QueuePath, Cipher());
        foreach (var e in events) first.Enqueue(e);
        first.Ack(new[] { events[0].ClientEventId, events[2].ClientEventId });

        // "Crash" and relaunch: a fresh instance (fresh key load) sees exactly what is still owed.
        var reopened = new MonitoringEventQueue(QueuePath, Cipher());
        reopened.PeekBatch(10).Should().Equal(events[1], events[3]);
        reopened.Enqueue(Event(9, urgent: true));
        reopened.HasUrgent.Should().BeTrue();
        new MonitoringEventQueue(QueuePath, Cipher()).Count.Should().Be(3);

        reopened.DeleteFile();
        File.Exists(QueuePath).Should().BeFalse();
    }

    [Fact]
    public void Queue_SkipsEditedRecords_AndDropsATornTail()
    {
        var queue = new MonitoringEventQueue(QueuePath, Cipher());
        var events = Enumerable.Range(0, 3).Select(i => Event(i)).ToList();
        foreach (var e in events) queue.Enqueue(e);

        var bytes = File.ReadAllBytes(QueuePath);
        // Flip a byte inside the first record's ciphertext (header 5 + length 4 + nonce/tag 28 + a little).
        bytes[5 + 4 + 30] ^= 0xFF;
        // And simulate a crash mid-append: half a length prefix at the end.
        File.WriteAllBytes(QueuePath, bytes.Concat(new byte[] { 0x10, 0x00 }).ToArray());

        var reopened = new MonitoringEventQueue(QueuePath, Cipher());
        reopened.RejectedRecords.Should().Be(1);
        reopened.PeekBatch(10).Should().Equal(events[1], events[2]);
        new MonitoringEventQueue(QueuePath, Cipher()).RejectedRecords.Should().Be(0, "the file was rewritten without the bad record");
    }

    [Fact]
    public void Queue_FromAnotherSession_DoesNotOpen()
    {
        var queue = new MonitoringEventQueue(QueuePath, Cipher());
        queue.Enqueue(Event(0));
        var otherSession = Guid.NewGuid();
        var key = SessionKeyFile.LoadOrCreate(Path.Combine(_dir, "key.bin"), _session, new FakeKeyProtector());

        // Same key, different session bound as associated data: the records are not accepted.
        var foreign = new MonitoringEventQueue(QueuePath, new RecordCipher(key, otherSession));
        foreign.Count.Should().Be(0);
        foreign.RejectedRecords.Should().Be(1);
    }

    [Fact]
    public void SessionKey_WrappedForAnotherSession_IsRefused()
    {
        var path = Path.Combine(_dir, "key.bin");
        SessionKeyFile.LoadOrCreate(path, _session, new FakeKeyProtector());
        var act = () => SessionKeyFile.LoadOrCreate(path, Guid.NewGuid(), new FakeKeyProtector());
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void RealDpapi_WrapsAndUnwrapsTheSpoolKey()
    {
        var path = Path.Combine(_dir, "dpapi-key.bin");
        var key = SessionKeyFile.LoadOrCreate(path, _session, new DpapiKeyProtector());
        SessionKeyFile.LoadOrCreate(path, _session, new DpapiKeyProtector()).Should().Equal(key);
    }

    [Fact]
    public void CaptureSpool_RoundTripsEncrypted_OldestFirst()
    {
        var spool = new CaptureSpool(Path.Combine(_dir, "captures"), Cipher());
        var image = Encoding.ASCII.GetBytes("JPEG-PIXELS-MARKER").Concat(new byte[200]).ToArray();
        spool.Add(new CaptureInfo(Guid.NewGuid(), "Screen", "Interval", null, T0.AddSeconds(30)), image);
        spool.Add(new CaptureInfo(Guid.NewGuid(), "Webcam", "Violation", "CameraCovered", T0), image);

        var pending = spool.Pending();
        pending.Should().HaveCount(2);
        var first = spool.Read(pending[0])!.Value;
        first.Info.Kind.Should().Be("Webcam");
        first.Info.Reason.Should().Be("CameraCovered");
        first.Image.Should().Equal(image);
        Encoding.ASCII.GetString(File.ReadAllBytes(pending[0])).Should().NotContain("JPEG-PIXELS-MARKER").And.NotContain("CameraCovered");

        spool.Remove(pending[0]);
        spool.Count.Should().Be(1);
    }

    [Fact]
    public void CaptureSpool_WhenFull_DropsIntervalCapturesBeforeViolationEvidence()
    {
        var spool = new CaptureSpool(Path.Combine(_dir, "captures"), Cipher(), maxFiles: 3);
        spool.Add(new CaptureInfo(Guid.NewGuid(), "Screen", "Violation", "ProcessKilled", T0), new byte[] { 1 });
        spool.Add(new CaptureInfo(Guid.NewGuid(), "Screen", "Interval", null, T0.AddSeconds(1)), new byte[] { 2 });
        spool.Add(new CaptureInfo(Guid.NewGuid(), "Screen", "Interval", null, T0.AddSeconds(2)), new byte[] { 3 });
        spool.Add(new CaptureInfo(Guid.NewGuid(), "Screen", "Violation", "FocusLoss", T0.AddSeconds(3)), new byte[] { 4 });

        spool.Dropped.Should().Be(1);
        var kept = spool.Pending().Select(p => spool.Read(p)!.Value.Image[0]).ToList();
        kept.Should().Equal(1, 3, 4);
    }

    [Fact]
    public void CaptureSpool_TamperedFile_IsDiscarded()
    {
        var spool = new CaptureSpool(Path.Combine(_dir, "captures"), Cipher());
        spool.Add(new CaptureInfo(Guid.NewGuid(), "Screen", "Interval", null, T0), new byte[64]);
        var path = spool.Pending().Single();
        var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0x01;
        File.WriteAllBytes(path, bytes);

        spool.Read(path).Should().BeNull();
        spool.Count.Should().Be(0, "an altered capture is never uploaded");
    }

    [Fact]
    public void MonitoringSpool_ReopensASession_AndResetsWhenTheKeyNoLongerOpens()
    {
        var first = MonitoringSpool.Open(_dir, _session, new FakeKeyProtector());
        first.Events.Enqueue(Event(1));
        first.WasReset.Should().BeFalse();

        var again = MonitoringSpool.Open(_dir, _session, new FakeKeyProtector());
        again.Events.Count.Should().Be(1);
        again.IsEmpty.Should().BeFalse();

        // Key file edited (or the folder copied to another Windows account): start afresh and say so.
        var keyPath = Path.Combine(again.Directory, "key.bin");
        var key = File.ReadAllBytes(keyPath);
        key[^1] ^= 0xFF;
        File.WriteAllBytes(keyPath, key);
        var reset = MonitoringSpool.Open(_dir, _session, new FakeKeyProtector());
        reset.WasReset.Should().BeTrue();
        reset.IsEmpty.Should().BeTrue();

        reset.Delete();
        Directory.Exists(reset.Directory).Should().BeFalse();
    }

    [Fact]
    public void MonitoringSpool_Cleanup_RemovesOnlyStaleSessionFolders()
    {
        var stale = MonitoringSpool.Open(_dir, Guid.NewGuid(), new FakeKeyProtector());
        stale.Events.Enqueue(Event(1));
        var fresh = MonitoringSpool.Open(_dir, Guid.NewGuid(), new FakeKeyProtector());
        fresh.Events.Enqueue(Event(2));
        foreach (var f in Directory.EnumerateFiles(stale.Directory, "*", SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddDays(-10));

        MonitoringSpool.Cleanup(_dir, TimeSpan.FromDays(7), DateTime.UtcNow).Should().Be(1);
        Directory.Exists(stale.Directory).Should().BeFalse();
        Directory.Exists(fresh.Directory).Should().BeTrue();
    }
}
