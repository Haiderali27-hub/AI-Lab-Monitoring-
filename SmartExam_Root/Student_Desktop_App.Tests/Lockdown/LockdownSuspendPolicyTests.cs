using FluentAssertions;
using Student_Desktop_App.Core.Lockdown;

namespace Student_Desktop_App.Tests.Lockdown;

/// <summary>
/// Module 10 manual unlock on the desktop: the server's unlock deadline (server clock) converted to this PC's clock,
/// and the suspend / reschedule / resume decisions the exam window takes on each heartbeat ack.
/// </summary>
public class LockdownSuspendPolicyTests
{
    private static readonly DateTime Server = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void M10_U_05_LocalDeadline_UsesTheRemainingServerTime_NotThePcClock()
    {
        // The PC clock is 7 minutes behind the server: it must still re-lock after exactly 5 minutes.
        var local = Server.AddMinutes(-7);
        LockdownSuspendPolicy.LocalDeadline(Server.AddMinutes(5), Server, local).Should().Be(local.AddMinutes(5));
    }

    [Fact]
    public void M10_U_06_LocalDeadline_NullWhenLockedOrExpired_AndCappedAt30Minutes()
    {
        LockdownSuspendPolicy.LocalDeadline(null, Server, Server).Should().BeNull();
        LockdownSuspendPolicy.LocalDeadline(Server.AddSeconds(-1), Server, Server).Should().BeNull();
        LockdownSuspendPolicy.LocalDeadline(Server, Server, Server).Should().BeNull();
        LockdownSuspendPolicy.LocalDeadline(Server.AddHours(5), Server, Server).Should().Be(Server.AddMinutes(30),
            "a tampered or wrong ack never opens the PC for longer than the hub allows");
    }

    [Fact]
    public void M10_U_07_Decide_SuspendRescheduleResume()
    {
        var now = Server;
        var in5 = now.AddMinutes(5);

        LockdownSuspendPolicy.Decide(null, null, now).Should().Be(SuspendAction.None);
        LockdownSuspendPolicy.Decide(null, in5, now).Should().Be(SuspendAction.Suspend);
        LockdownSuspendPolicy.Decide(in5, in5.AddSeconds(2), now).Should().Be(SuspendAction.None, "heartbeat jitter is not a new unlock");
        LockdownSuspendPolicy.Decide(in5, now.AddMinutes(10), now).Should().Be(SuspendAction.Reschedule, "a new unlock replaced the deadline");
        LockdownSuspendPolicy.Decide(in5, null, now).Should().Be(SuspendAction.Resume, "the proctor relocked early");
        LockdownSuspendPolicy.Decide(now.AddSeconds(-1), null, now).Should().Be(SuspendAction.Resume, "the deadline passed");
        LockdownSuspendPolicy.Decide(now.AddSeconds(-1), in5, now).Should().Be(SuspendAction.Suspend, "unlocked again after it expired");
    }
}
