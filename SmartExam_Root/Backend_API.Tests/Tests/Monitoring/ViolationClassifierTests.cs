using Backend_API.Models.Enums;
using Backend_API.Services;
using FluentAssertions;
using T = Backend_API.Models.Enums.MonitoringEventType;

namespace Backend_API.Tests.Tests.Monitoring;

public class ViolationClassifierTests
{
    [Fact]
    public void NoViolations_IsNormal() =>
        ViolationClassifier.Classify(new[] { T.Heartbeat, T.WindowFocus }).Should().Be("Normal");

    [Fact]
    public void WarningsEscalateToSuspiciousAtThree()
    {
        ViolationClassifier.Classify(new[] { T.KeyBlocked }).Should().Be("Warning");
        ViolationClassifier.Classify(new[] { T.KeyBlocked, T.FocusLost }).Should().Be("Warning");
        ViolationClassifier.Classify(new[] { T.KeyBlocked, T.FocusLost, T.ClipboardBlocked }).Should().Be("Suspicious");
    }

    [Fact]
    public void SuspiciousEscalatesToViolationAtTwo()
    {
        ViolationClassifier.Classify(new[] { T.UnauthorizedProcess }).Should().Be("Suspicious");
        ViolationClassifier.Classify(new[] { T.UnauthorizedProcess, T.MultiMonitor }).Should().Be("Violation");
    }

    [Theory]
    [InlineData(T.ProcessKilled)]
    [InlineData(T.UsbInserted)]
    [InlineData(T.RemoteSession)]
    [InlineData(T.Violation)]   // legacy generic violation keeps showing red
    public void SevereTypes_AreViolationImmediately(T type) =>
        ViolationClassifier.Classify(new[] { type }).Should().Be("Violation");

    [Theory]
    [InlineData("ProcessKilled", null, T.ProcessKilled)]
    [InlineData("processkilled", null, T.ProcessKilled)]
    [InlineData("Violation", "{\"reason\":\"KeyBlocked\",\"detail\":\"Alt+Tab\"}", T.KeyBlocked)]
    [InlineData("Violation", "{\"reason\":\"FocusLoss\"}", T.FocusLost)]           // older client spelling
    [InlineData("Violation", "{\"reason\":\"SomethingNew\"}", T.Violation)]
    [InlineData("Violation", "not json", T.Violation)]
    [InlineData("Attendance", "{}", T.WindowFocus)]                                // pre-exam check-ins stay non-violations
    [InlineData("Heartbeat", "{}", T.WindowFocus)]                                 // heartbeats go through their own endpoint
    public void Resolve_MapsClientEventsToStoredTypes(string eventType, string? payload, T expected) =>
        ViolationClassifier.Resolve(eventType, payload).Should().Be(expected);
}
