using Backend_API.Services;
using Backend_API.Tests.Tests.Acceptance;
using FluentAssertions;

namespace Backend_API.Tests.Tests.Monitoring;

/// <summary>M7-U-03 — ExamGroupTracker: which live connections to take out of which exam group.</summary>
public class ExamGroupTrackerTests
{
    private readonly HubRecorder _hub = new();
    private readonly Guid _alice = Guid.NewGuid(), _bob = Guid.NewGuid();
    private readonly Guid _examA = Guid.NewGuid(), _examB = Guid.NewGuid();

    [Fact]
    public async Task RevokeOneExam_RemovesOnlyThatUsersConnectionsInThatGroup()
    {
        var tracker = new ExamGroupTracker(_hub);
        tracker.Joined("a1", _alice, _examA);
        tracker.Joined("a1", _alice, _examB);
        tracker.Joined("a2", _alice, _examA); // a second tab
        tracker.Joined("b1", _bob, _examA);

        (await tracker.RevokeAsync(_alice, _examA)).Should().Be(2);

        _hub.Groups.Removed.Should().BeEquivalentTo(new[] { ("a1", $"exam:{_examA}"), ("a2", $"exam:{_examA}") });
        _hub.Named(ExamGroupTracker.RevokedMethod).Select(m => m.Target).Should().BeEquivalentTo(new[] { "conn:a1", "conn:a2" });
        tracker.MembershipsOf(_alice).Should().BeEquivalentTo(new[] { ("a1", _examB) });
        tracker.MembershipsOf(_bob).Should().ContainSingle();
    }

    [Fact]
    public async Task RevokeAll_LeftAndDisconnected_AreNotRevokedAgain()
    {
        var tracker = new ExamGroupTracker(_hub);
        tracker.Joined("a1", _alice, _examA);
        tracker.Joined("a1", _alice, _examB);
        tracker.Joined("a2", _alice, _examA);
        tracker.Left("a1", _examB);      // the page switched exams
        tracker.Disconnected("a2");      // the tab closed

        (await tracker.RevokeAsync(_alice)).Should().Be(1);
        _hub.Groups.Removed.Should().ContainSingle().Which.Should().Be(("a1", $"exam:{_examA}"));

        (await tracker.RevokeAsync(_alice)).Should().Be(0, "nothing is left to revoke");
        (await tracker.RevokeAsync(_bob)).Should().Be(0);
    }
}
