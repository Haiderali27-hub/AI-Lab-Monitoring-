using FluentAssertions;
using Student_Desktop_App.Core.Monitoring;

namespace Student_Desktop_App.Tests.Monitoring;

/// <summary>
/// Module 10 (D-05) — the exam window's proctor-warning inbox: the server repeats unread warnings in every heartbeat
/// ack, the banner must show each one once, in order, until the student acknowledges it.
/// </summary>
public class WarningInboxTests
{
    private static readonly DateTime T0 = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    private static ProctorWarning Warning(int minute, string body = "Eyes on your own screen") =>
        new(Guid.NewGuid(), "Proctor Warning", body, T0.AddMinutes(minute));

    [Fact]
    public void M10_U_01_RepeatedAck_RaisesEachWarningOnce()
    {
        var inbox = new WarningInbox();
        var w = Warning(1);

        inbox.Accept([w]).Should().ContainSingle().Which.Should().Be(w);
        inbox.Accept([w]).Should().BeEmpty("the next heartbeat repeats the unread warning");
        inbox.Accept([w]).Should().BeEmpty();
        inbox.Current.Should().Be(w);
    }

    [Fact]
    public void M10_U_02_PendingWarnings_AreShownOldestFirst_UntilAcknowledged()
    {
        var inbox = new WarningInbox();
        var older = Warning(1, "first");
        var newer = Warning(2, "second");

        inbox.Accept([newer, older]).Should().Equal(older, newer);
        inbox.Current.Should().Be(older);

        inbox.Acknowledge(older.NotificationId).Should().BeTrue();
        inbox.Current.Should().Be(newer);
        inbox.Pending.Should().ContainSingle();

        inbox.Acknowledge(newer.NotificationId).Should().BeTrue();
        inbox.Current.Should().BeNull();
        inbox.Acknowledge(newer.NotificationId).Should().BeFalse();
    }

    [Fact]
    public void M10_U_03_AcknowledgedWarning_StaysGone_EvenWhenTheServerStillSendsIt()
    {
        // Mark-read failed (offline): the server keeps sending it, the student is not asked again.
        var inbox = new WarningInbox();
        var w = Warning(1);
        inbox.Accept([w]);
        inbox.Acknowledge(w.NotificationId);

        inbox.Accept([w]).Should().BeEmpty();
        inbox.Current.Should().BeNull();
    }

    [Fact]
    public void M10_U_04_EmptyOrMalformedWarnings_AreIgnored()
    {
        var inbox = new WarningInbox();

        inbox.Accept(null).Should().BeEmpty();
        inbox.Accept([new ProctorWarning(Guid.Empty, "Proctor Warning", "x", T0), new ProctorWarning(Guid.NewGuid(), "Proctor Warning", "  ", T0)])
            .Should().BeEmpty();
        inbox.Current.Should().BeNull();
    }
}
