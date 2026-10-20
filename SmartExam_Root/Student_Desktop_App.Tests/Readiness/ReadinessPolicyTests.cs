using FluentAssertions;
using Student_Desktop_App.Core.Lockdown;
using Student_Desktop_App.Core.Readiness;
using Student_Desktop_App.Services;
using Student_Desktop_App.Services.Readiness;

namespace Student_Desktop_App.Tests.Readiness;

/// <summary>M2-U-* — pre-exam readiness rules (TEST_CASES.md §M2, layer U).</summary>
public class ReadinessPolicyTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly ReadinessRequirements Default = new(false, false, true, LockdownMode.Report);

    [Fact]
    public void M2_U_01_Network_FastPasses_SlowOrUnreachableFails()
    {
        ReadinessPolicy.EvaluateNetwork(new TimeSpan?[] { TimeSpan.FromMilliseconds(300) })
            .Should().Match<ReadinessItemResult>(r => r.Passed && r.Detail == "300 ms");

        ReadinessPolicy.EvaluateNetwork(new TimeSpan?[] { TimeSpan.FromMilliseconds(3000) }).Passed.Should().BeFalse();
        ReadinessPolicy.EvaluateNetwork(new TimeSpan?[] { null, null, null })
            .Should().Match<ReadinessItemResult>(r => !r.Passed && r.Detail.Contains("unreachable"));

        // One failed attempt followed by a fast one is fine (best of 3)
        ReadinessPolicy.EvaluateNetwork(new TimeSpan?[] { null, TimeSpan.FromMilliseconds(120) }).Passed.Should().BeTrue();
    }

    [Fact]
    public void M2_U_02_ClockSkew_10sPasses_120sFails_MissingFails()
    {
        var server = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);
        ReadinessPolicy.EvaluateClockSkew(server, server.AddSeconds(10)).Passed.Should().BeTrue();
        ReadinessPolicy.EvaluateClockSkew(server, server.AddSeconds(-120)).Passed.Should().BeFalse();
        ReadinessPolicy.EvaluateClockSkew(null, server).Passed.Should().BeFalse();
    }

    [Fact]
    public void M2_U_03_CameraAndMic_SkippedWhenNotRequired_FailWhenRequiredAndMissing()
    {
        var skipped = ReadinessPolicy.EvaluateDevice(ReadinessPolicy.Camera, required: false, deviceCount: 0);
        skipped.Passed.Should().BeTrue();
        skipped.Skipped.Should().BeTrue();

        ReadinessPolicy.EvaluateDevice(ReadinessPolicy.Camera, true, 0).Should().Match<ReadinessItemResult>(r => !r.Passed && r.Detail == "No device found");
        ReadinessPolicy.EvaluateDevice(ReadinessPolicy.Microphone, true, 2).Passed.Should().BeTrue();
        ReadinessPolicy.EvaluateDevice(ReadinessPolicy.Camera, true, 1, "Integrated Webcam").Detail.Should().Be("Integrated Webcam");
    }

    [Fact]
    public void M2_U_04_MultiMonitor_FailsSecureMode_OnlyWhenBlocked()
    {
        var fail = ReadinessPolicy.EvaluateSecureMode(Default, monitorCount: 2, isRemoteSession: false, virtualMachineVendor: null, keyboardHookOk: true);
        fail.Passed.Should().BeFalse();
        fail.Detail.Should().Contain("2 monitors");

        ReadinessPolicy.EvaluateSecureMode(Default with { BlockMultiMonitor = false }, 2, false, null, true).Passed.Should().BeTrue();
    }

    [Fact]
    public void M2_U_05_RemoteSession_VirtualMachine_OrHookFailure_FailSecureMode()
    {
        ReadinessPolicy.EvaluateSecureMode(Default, 1, isRemoteSession: true, null, true)
            .Should().Match<ReadinessItemResult>(r => !r.Passed && r.Detail.Contains("Remote Desktop"));
        ReadinessPolicy.EvaluateSecureMode(Default, 1, false, "VMware", true)
            .Should().Match<ReadinessItemResult>(r => !r.Passed && r.Detail.Contains("VMware"));
        ReadinessPolicy.EvaluateSecureMode(Default, 1, false, null, keyboardHookOk: false).Passed.Should().BeFalse();

        // Several problems are all listed
        ReadinessPolicy.EvaluateSecureMode(Default, 3, true, "Hyper-V", false).Detail.Split(';').Should().HaveCount(4);

        // Lockdown off (local practice): secure mode is skipped
        var off = ReadinessPolicy.EvaluateSecureMode(Default with { Mode = LockdownMode.Off }, 3, true, "VMware", false);
        off.Passed.Should().BeTrue();
        off.Skipped.Should().BeTrue();
    }

    [Fact]
    public void M2_U_06_PassedAll_OnlyWhenEveryItemPasses_SkippedCounts()
    {
        var ok = new ReadinessItemResult("Network", true, "ok");
        var skipped = new ReadinessItemResult("Camera", true, "Not required", Skipped: true);
        var bad = new ReadinessItemResult("Disk", false, "low");

        ReadinessPolicy.PassedAll(new[] { ok, skipped }).Should().BeTrue();
        ReadinessPolicy.PassedAll(new[] { ok, skipped, bad }).Should().BeFalse();
        ReadinessPolicy.PassedAll(Array.Empty<ReadinessItemResult>()).Should().BeFalse("an empty checklist proves nothing");
    }

    [Fact]
    public void M2_U_07_Disk_NeedsAtLeast200MB()
    {
        ReadinessPolicy.EvaluateDisk(ReadinessPolicy.MinFreeDiskBytes).Passed.Should().BeTrue();
        ReadinessPolicy.EvaluateDisk(ReadinessPolicy.MinFreeDiskBytes - 1).Passed.Should().BeFalse();
        ReadinessPolicy.EvaluateDisk(12L * 1024 * 1024 * 1024).Detail.Should().Be("12.0 GB free");
    }

    [Theory]
    [InlineData("VMware, Inc.", "VMware Virtual Platform", "VMware")]
    [InlineData("innotek GmbH", "VirtualBox", "VirtualBox")]
    [InlineData("Microsoft Corporation", "Virtual Machine", "Hyper-V")]
    [InlineData("QEMU", "Standard PC (Q35 + ICH9, 2009)", "QEMU/KVM")]
    [InlineData("Dell Inc.", "Latitude 5420", null)]
    [InlineData("Microsoft Corporation", "Surface Laptop 5", null)]
    [InlineData(null, null, null)]
    public void M4_U_14_VirtualMachineDetection(string? manufacturer, string? model, string? expected) =>
        ReadinessPolicy.DetectVirtualMachine(manufacturer, model).Should().Be(expected);

    /// Runs the real Win32/WMI probes on this machine (no exam server needed). Network and clock
    /// fail without a running backend; the point is that every check returns a row and nothing throws.
    [Fact]
    public async Task M2_U_08_Service_RunsEveryCheck_OnThisMachine_WithoutThrowing()
    {
        var service = new ReadinessCheckService(new ApiClient());
        var results = await service.RunAllAsync(new ReadinessRequirements(true, true, true, LockdownMode.Report));
        foreach (var r in results)
            output.WriteLine($"{(r.Passed ? "PASS" : "FAIL"),-4} {r.Name,-14} {r.DurationMs,5} ms  {r.Detail}");

        results.Select(r => r.Name).Should().Equal(ReadinessPolicy.AllChecks);
        results.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Detail));
        results.Single(r => r.Name == ReadinessPolicy.Disk).Passed.Should().BeTrue("the dev machine has > 200 MB free");
        results.Should().NotContain(r => r.Detail.StartsWith("Check failed"), "no probe should throw");
    }
}
