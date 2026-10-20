using FluentAssertions;
using Student_Desktop_App.Core.Dashboard;

namespace Student_Desktop_App.Tests.Dashboard;

/// <summary>
/// M3-D-01 logic (TEST_CASES.md §M3) — the dashboard Start button follows the SERVER clock:
/// disabled before the exam opens (StartTime − EarlyJoinMinutes), after it ends, for a running or
/// finished session, and when the server reports a seat mismatch.
/// </summary>
public class StartGateTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    private static StartGateInput Exam(DateTime start, int durationMinutes = 60, int earlyJoin = 0, bool eligible = true,
        string status = "NotStarted", string? seat = null) =>
        new(eligible, status, StartGate.OpensAt(start, earlyJoin, null), start.AddMinutes(durationMinutes), seat);

    [Fact]
    public void M3_D_01_BeforeStart_Disabled_AtStart_Enabled()
    {
        var exam = Exam(Now.AddMinutes(10));

        StartGate.CanStart(exam, TimeSpan.Zero, Now).Should().BeFalse();
        StartGate.UntilOpen(exam.OpensAtUtc, TimeSpan.Zero, Now).Should().Be(TimeSpan.FromMinutes(10));

        StartGate.CanStart(exam, TimeSpan.Zero, Now.AddMinutes(10)).Should().BeTrue();
        StartGate.UntilOpen(exam.OpensAtUtc, TimeSpan.Zero, Now.AddMinutes(11)).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void M3_D_01_EarlyJoinWindow_OpensBeforeStartTime()
    {
        var exam = Exam(Now.AddMinutes(3), earlyJoin: 5);
        StartGate.CanStart(exam, TimeSpan.Zero, Now).Should().BeTrue("T-3 is inside a 5-minute early-join window");
        StartGate.CanStart(Exam(Now.AddMinutes(3)), TimeSpan.Zero, Now).Should().BeFalse();
    }

    [Fact]
    public void M3_D_01_UsesServerClock_NotTheLocalClock()
    {
        // The PC's clock was moved 30 minutes ahead; the server says it is really 09:00.
        var localAhead = Now.AddMinutes(30);
        var offset = StartGate.ClockOffset(serverTimeUtc: Now, localUtcAtFetch: localAhead);
        offset.Should().Be(TimeSpan.FromMinutes(-30));

        var exam = Exam(Now.AddMinutes(10));
        StartGate.CanStart(exam, offset, localAhead).Should().BeFalse("on the server clock the exam opens in 10 minutes");
        StartGate.CanStart(exam, TimeSpan.Zero, localAhead).Should().BeTrue("trusting the local clock would unlock it");
        StartGate.UntilOpen(exam.OpensAtUtc, offset, localAhead).Should().Be(TimeSpan.FromMinutes(10));

        StartGate.ClockOffset(null, localAhead).Should().Be(TimeSpan.Zero, "older servers send no serverTimeUtc");
    }

    [Theory]
    [InlineData(false, "NotStarted", null)]      // not eligible
    [InlineData(true, "InProgress", null)]       // already running
    [InlineData(true, "Submitted", null)]
    [InlineData(true, "ForceSubmitted", null)]
    [InlineData(true, "NotStarted", "Mismatch")] // wrong seat (server-reported)
    public void M3_D_01_OpenExam_StillBlockedFor(bool eligible, string status, string? seat) =>
        StartGate.CanStart(Exam(Now.AddMinutes(-5), eligible: eligible, status: status, seat: seat), TimeSpan.Zero, Now).Should().BeFalse();

    [Fact]
    public void M3_D_01_AfterEnd_Disabled_AndSeatOkOrUnassignedAllowed()
    {
        StartGate.CanStart(Exam(Now.AddMinutes(-61)), TimeSpan.Zero, Now).Should().BeFalse("the 60-minute window is over");
        StartGate.CanStart(Exam(Now.AddMinutes(-5), seat: "Ok"), TimeSpan.Zero, Now).Should().BeTrue();
        StartGate.CanStart(Exam(Now.AddMinutes(-5), seat: "Unassigned"), TimeSpan.Zero, Now).Should().BeTrue();
    }

    [Fact]
    public void ServerOpensAt_WinsOverLocalComputation_AndFormatting()
    {
        var serverOpens = Now.AddMinutes(-2);
        StartGate.OpensAt(Now.AddMinutes(10), 0, serverOpens).Should().Be(serverOpens);
        StartGate.OpensAt(null, 5, null).Should().BeNull();
        StartGate.HasOpened(null, TimeSpan.Zero, Now).Should().BeFalse("no start time known → never open");

        StartGate.Format(TimeSpan.FromSeconds(3725)).Should().Be("01:02:05");
        StartGate.Format(TimeSpan.FromHours(26)).Should().Be("1.02:00:00");
    }

    // M6-U-09 (TEST_CASES.md §M6, D-18): a session still running on the server is offered as "Resume" —
    // no new check-in — until the student's own end (which includes extra minutes) on the server clock.
    [Fact]
    public void M6_U_09_RunningSession_CanBeResumed_UntilTheStudentsEnd()
    {
        var running = Exam(Now.AddMinutes(-30), status: "InProgress");
        StartGate.CanStart(running, TimeSpan.Zero, Now).Should().BeFalse("it is not a new start");
        StartGate.CanResume(running, TimeSpan.Zero, Now).Should().BeTrue();
        StartGate.CanResume(running, TimeSpan.Zero, Now.AddMinutes(31)).Should().BeFalse("the student's time is over");
        StartGate.CanResume(running, TimeSpan.FromMinutes(31), Now).Should().BeFalse("server clock, not the PC clock");

        StartGate.CanResume(Exam(Now.AddMinutes(-30)), TimeSpan.Zero, Now).Should().BeFalse("nothing to resume");
        StartGate.CanResume(Exam(Now.AddMinutes(-30), status: "Submitted"), TimeSpan.Zero, Now).Should().BeFalse();
        StartGate.CanResume(Exam(Now.AddMinutes(-30), status: "InProgress", seat: "Mismatch"), TimeSpan.Zero, Now).Should().BeFalse();
        StartGate.CanResume(Exam(Now.AddMinutes(-30), status: "InProgress", eligible: false), TimeSpan.Zero, Now).Should().BeFalse();
    }
}
