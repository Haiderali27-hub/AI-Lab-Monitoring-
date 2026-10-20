using System.IO;
using System.Security.Cryptography;
using FluentAssertions;
using Student_Desktop_App.Core.Backup;
using Student_Desktop_App.Services.Backup;

namespace Student_Desktop_App.Tests.Backup;

/// <summary>
/// M6-U-03 / M6-U-04 at file level, plus crash-recovery housekeeping (M6-U-08). Uses a temp directory and a fake
/// key protector; one test exercises the real DPAPI wrapper (harmless: CurrentUser scope, no files outside temp).
/// </summary>
public sealed class AnswerBackupStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smartexam-backup-tests-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _exam = Guid.NewGuid();

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Stand-in for DPAPI: "wraps" by XOR with the entropy and appends a checksum, throws on any mismatch.</summary>
    private sealed class FakeProtector : IKeyProtector
    {
        public int Wraps;

        public byte[] Protect(byte[] key, byte[] entropy)
        {
            Wraps++;
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

    private AnswerBackupStore NewStore(IKeyProtector? protector = null) => new(_dir, protector ?? new FakeProtector(), () => _now);

    private BackupState State(Guid? session = null, Guid? user = null, params BackupAnswer[] answers) =>
        new(session ?? Guid.NewGuid(), _exam, user ?? _user, _now,
            answers.Length > 0 ? answers : new[] { new BackupAnswer(Guid.NewGuid(), "my answer", 3, 1) });

    [Fact]
    public void M6_U_03_SaveThenLoad_RoundTrips_AndTheFileIsEncrypted()
    {
        var store = NewStore();
        var state = State();
        store.Save(state);

        var loaded = NewStore().Load(state.SessionId);   // a fresh instance = the relaunched app
        loaded.Status.Should().Be(BackupLoadStatus.Ok);
        loaded.State!.Answers.Should().Equal(state.Answers);
        loaded.State.SavedAtUtc.Should().Be(_now);

        File.ReadAllText(store.PathFor(state.SessionId)).Should().NotContain("my answer");
        Directory.GetFiles(_dir, "*.tmp").Should().BeEmpty("the write is atomic (.tmp then replace)");
        store.Load(Guid.NewGuid()).Status.Should().Be(BackupLoadStatus.Missing);
    }

    [Fact]
    public void RepeatedSaves_ReuseTheSessionKey_AndOverwriteTheFile()
    {
        var protector = new FakeProtector();
        var store = NewStore(protector);
        var session = Guid.NewGuid();
        var q = Guid.NewGuid();
        store.Save(State(session, null, new BackupAnswer(q, "v1", 1, 0)));
        store.Save(State(session, null, new BackupAnswer(q, "v2", 2, 1)));

        protector.Wraps.Should().Be(1);
        store.Load(session).State!.Answers.Single().Text.Should().Be("v2");
        Directory.GetFiles(_dir).Should().ContainSingle();
    }

    [Fact]
    public void M6_U_04_FlippedByte_InPayloadOrWrappedKey_IsReportedAsTampered_NotRestored()
    {
        var store = NewStore();
        var state = State();
        store.Save(state);
        var path = store.PathFor(state.SessionId);
        var original = File.ReadAllBytes(path);

        foreach (var index in new[] { 0, 4, 12, original.Length - 1, original.Length / 2 })
        {
            var copy = (byte[])original.Clone();
            copy[index] ^= 0x01;
            File.WriteAllBytes(path, copy);

            var result = NewStore().Load(state.SessionId);
            result.Status.Should().Be(BackupLoadStatus.Tampered, $"byte {index} was flipped");
            result.State.Should().BeNull();
            result.Error.Should().NotBeNullOrWhiteSpace();
        }

        // A file renamed to another session's name is rejected as well (key entropy + associated data).
        File.WriteAllBytes(path, original);
        var other = Guid.NewGuid();
        File.Copy(path, store.PathFor(other));
        NewStore().Load(other).Status.Should().Be(BackupLoadStatus.Tampered);

        // After a tamper the next save starts a clean file that loads again.
        File.WriteAllBytes(path, original.Select(b => (byte)~b).ToArray());
        var fresh = NewStore();
        fresh.Load(state.SessionId).Status.Should().Be(BackupLoadStatus.Tampered);
        fresh.Save(state);
        NewStore().Load(state.SessionId).Status.Should().Be(BackupLoadStatus.Ok);
    }

    [Fact]
    public void M6_U_08_FindResumable_ReturnsOnlyThisStudentsUnsubmittedBackups()
    {
        var store = NewStore();
        var mine = State();
        var submitted = State();
        var someoneElse = State(user: Guid.NewGuid());
        store.Save(mine);
        store.Save(submitted);
        store.Save(someoneElse);
        store.MarkSubmitted(submitted.SessionId, new BackupReceipt(_now, 1, "ab", "ab", true));
        File.WriteAllBytes(store.PathFor(Guid.NewGuid()), new byte[] { 1, 2, 3 });   // garbage is skipped

        var resumable = NewStore().FindResumable(_user);
        resumable.Should().ContainSingle().Which.SessionId.Should().Be(mine.SessionId);

        var receipt = store.Load(submitted.SessionId).State!;
        receipt.Submitted.Should().BeTrue();
        receipt.Receipt!.Verified.Should().BeTrue();
    }

    [Fact]
    public void M6_U_08_Cleanup_DeletesOldSubmittedAndAbandonedBackups_KeepsRecentOnes()
    {
        var store = NewStore();
        var oldSubmitted = State();
        var oldUnsubmitted = State();
        var abandoned = State();
        var recentSubmitted = State();
        foreach (var s in new[] { oldSubmitted, oldUnsubmitted, abandoned, recentSubmitted }) store.Save(s);
        store.MarkSubmitted(oldSubmitted.SessionId, null);
        store.MarkSubmitted(recentSubmitted.SessionId, null);
        File.WriteAllBytes(store.PathFor(oldSubmitted.SessionId) + ".tmp", new byte[] { 0 });

        File.SetLastWriteTimeUtc(store.PathFor(oldSubmitted.SessionId), _now.AddDays(-8));
        File.SetLastWriteTimeUtc(store.PathFor(oldUnsubmitted.SessionId), _now.AddDays(-8));
        File.SetLastWriteTimeUtc(store.PathFor(abandoned.SessionId), _now.AddDays(-31));
        File.SetLastWriteTimeUtc(store.PathFor(recentSubmitted.SessionId), _now.AddDays(-1));

        store.Cleanup(TimeSpan.FromDays(7), TimeSpan.FromDays(30)).Should().Be(2);

        File.Exists(store.PathFor(oldSubmitted.SessionId)).Should().BeFalse();
        File.Exists(store.PathFor(abandoned.SessionId)).Should().BeFalse();
        File.Exists(store.PathFor(oldUnsubmitted.SessionId)).Should().BeTrue("an unfinished exam's answers are kept 30 days");
        File.Exists(store.PathFor(recentSubmitted.SessionId)).Should().BeTrue();
        Directory.GetFiles(_dir, "*.tmp").Should().BeEmpty();
        NewStore().Cleanup(TimeSpan.FromDays(7), TimeSpan.FromDays(30)).Should().Be(0);
    }

    [Fact]
    public void RealDpapiProtector_RoundTrips_AndRefusesAnotherSessionsEntropy()
    {
        var protector = new DpapiKeyProtector();
        var key = AnswerBackupCodec.NewKey();
        var entropy = Guid.NewGuid().ToByteArray();

        var wrapped = protector.Protect(key, entropy);
        wrapped.Should().NotEqual(key);
        protector.Unprotect(wrapped, entropy).Should().Equal(key);
        ((Action)(() => protector.Unprotect(wrapped, Guid.NewGuid().ToByteArray()))).Should().Throw<CryptographicException>();

        var store = NewStore(protector);
        var state = State();
        store.Save(state);
        new AnswerBackupStore(_dir, new DpapiKeyProtector()).Load(state.SessionId).Status.Should().Be(BackupLoadStatus.Ok);
    }
}
