using FluentAssertions;
using Student_Desktop_App.Core.Backup;

namespace Student_Desktop_App.Tests.Backup;

/// <summary>M6-U-03 / M6-U-04 (TEST_CASES.md §M6): AES-256-GCM sealed answer backup, with a fixed key.</summary>
public class AnswerBackupCodecTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly Guid Session = Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301");

    private static BackupState State() => new(
        Session, Guid.NewGuid(), Guid.NewGuid(), new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc),
        new[]
        {
            new BackupAnswer(Guid.NewGuid(), "int main() { return 0; } // ü✓", 7, 5),
            new BackupAnswer(Guid.NewGuid(), "2", 1, 1)
        });

    [Fact]
    public void M6_U_03_RoundTrip_RestoresTheSameAnswers()
    {
        var state = State();
        var decoded = AnswerBackupCodec.Decode(AnswerBackupCodec.Encode(state, Key), Key, Session);

        decoded.SessionId.Should().Be(state.SessionId);
        decoded.ExamId.Should().Be(state.ExamId);
        decoded.UserId.Should().Be(state.UserId);
        decoded.SavedAtUtc.Should().Be(state.SavedAtUtc);
        decoded.Answers.Should().Equal(state.Answers);
        decoded.UnsentCount.Should().Be(1);
    }

    [Fact]
    public void M6_U_03_Ciphertext_HidesTheAnswers_AndUsesAFreshNonceEachTime()
    {
        var state = State();
        var a = AnswerBackupCodec.Encode(state, Key);
        var b = AnswerBackupCodec.Encode(state, Key);

        System.Text.Encoding.UTF8.GetString(a).Should().NotContain("int main");
        a.Should().NotEqual(b, "a new random nonce per write");
    }

    [Fact]
    public void M6_U_04_FlippingAnyOneByte_IsDetected()
    {
        var sealedBytes = AnswerBackupCodec.Encode(State(), Key);
        // magic, nonce, tag, and a few ciphertext positions
        foreach (var index in new[] { 0, 4, 10, 16, 20, 31, 32, sealedBytes.Length / 2, sealedBytes.Length - 1 })
        {
            var copy = (byte[])sealedBytes.Clone();
            copy[index] ^= 0x01;
            var act = () => AnswerBackupCodec.Decode(copy, Key, Session);
            act.Should().Throw<BackupTamperedException>($"byte {index} was flipped");
        }
    }

    [Fact]
    public void M6_U_04_WrongKey_OtherSession_Truncated_AreAllRejected()
    {
        var sealedBytes = AnswerBackupCodec.Encode(State(), Key);
        var otherKey = (byte[])Key.Clone();
        otherKey[0] ^= 0xFF;

        ((Action)(() => AnswerBackupCodec.Decode(sealedBytes, otherKey, Session))).Should().Throw<BackupTamperedException>();
        ((Action)(() => AnswerBackupCodec.Decode(sealedBytes, Key, Guid.NewGuid()))).Should().Throw<BackupTamperedException>("a backup copied over from another session");
        ((Action)(() => AnswerBackupCodec.Decode(sealedBytes.AsSpan(0, 20), Key, Session))).Should().Throw<BackupTamperedException>();
        ((Action)(() => AnswerBackupCodec.Decode(Array.Empty<byte>(), Key, Session))).Should().Throw<BackupTamperedException>();
    }

    [Fact]
    public void KeyMustBe256Bits()
    {
        ((Action)(() => AnswerBackupCodec.Encode(State(), new byte[16]))).Should().Throw<ArgumentException>();
        AnswerBackupCodec.NewKey().Should().HaveCount(32);
    }
}
