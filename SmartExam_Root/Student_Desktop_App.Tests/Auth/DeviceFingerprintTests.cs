using FluentAssertions;
using Student_Desktop_App.Core;
using Student_Desktop_App.Core.Lockdown;

namespace Student_Desktop_App.Tests.Auth;

/// <summary>
/// Module 1 HWID v2 composition rules (pure; the WMI reads are not exercised). The server side of the
/// one-time v1 → v2 upgrade is M1-I-07 in Backend_API.Tests.
/// </summary>
public class DeviceFingerprintTests
{
    [Fact]
    public void ComposeV2_UsesBiosDiskAndBoardSerials()
    {
        DeviceFingerprint.ComposeV2("bios-sn-1234", " DISK-SN-5678 ", "BOARD-SN-9012")
            .Should().Be("v2|BIOS-SN-1234|DISK-SN-5678|BOARD-SN-9012");
    }

    [Fact]
    public void ComposeV2_IsDeterministic_AndIgnoresTheFallbacksWhenSerialsAreGood() =>
        DeviceFingerprint.ComposeV2("A1", "B2", "C3", "UUID-X", "GUID-Y")
            .Should().Be(DeviceFingerprint.ComposeV2("A1", "B2", "C3"));

    [Theory]
    [InlineData("To be filled by O.E.M.")]
    [InlineData("Default string")]
    [InlineData("0")]
    [InlineData("None")]
    [InlineData("00000000")]
    [InlineData("System Serial Number")]
    [InlineData("   ")]
    [InlineData(null)]
    public void CleanSerial_DropsOemPlaceholders(string? junk) =>
        DeviceFingerprint.CleanSerial(junk).Should().BeEmpty();

    [Fact]
    public void ComposeV2_WithMostlyJunkSerials_AppendsTheSmbiosUuid()
    {
        var fp = DeviceFingerprint.ComposeV2("To be filled by O.E.M.", "WD-123", "Default string", "4C4C4544-0042-3510-8051-B4C04F4E4E32");
        fp.Should().Be("v2||WD-123||uuid:4C4C4544-0042-3510-8051-B4C04F4E4E32");
    }

    [Fact]
    public void ComposeV2_WithJunkUuid_FallsBackToMachineGuid()
    {
        var fp = DeviceFingerprint.ComposeV2(null, null, "0", "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF", "1b2c3d4e-aaaa-bbbb-cccc-0123456789ab");
        fp.Should().Be("v2||||mguid:1B2C3D4E-AAAA-BBBB-CCCC-0123456789AB");
    }

    [Fact]
    public void ComposeV2_TwoJunkBoardsWithDifferentUuids_DoNotCollide() =>
        DeviceFingerprint.ComposeV2("0", "0", "0", "11111111-2222-3333-4444-555555555555")
            .Should().NotBe(DeviceFingerprint.ComposeV2("0", "0", "0", "66666666-7777-8888-9999-000000000000"));

    [Fact]
    public void ComposeV2_AlwaysCarriesTheVersionPrefix_AndNoStraySeparators()
    {
        var fp = DeviceFingerprint.ComposeV2("A|B", "C", "D");
        fp.Should().StartWith(DeviceFingerprint.V2Prefix);
        fp.Split('|').Should().HaveCount(4, "a '|' inside a serial must not create an extra field");
    }

    // ── Module 4: Task Manager and other escape tools (LockdownPolicy) ──────

    [Theory]
    [InlineData("taskmgr")]
    [InlineData("Taskmgr.exe")]
    [InlineData("procexp64")]
    [InlineData("SystemInformer")]
    [InlineData("regedit")]
    public void EscapeTools_AreKilledInEnforce_EvenWithoutAWindow(string name)
    {
        var policy = new LockdownPolicy(LockdownMode.Enforce, new[] { "code.exe" }, "Student_Desktop_App");
        policy.Evaluate(name, hasVisibleWindow: false).Should().Be(ProcessDecision.Kill);
        LockdownPolicy.IsEscapeTool(name).Should().BeTrue();
    }

    [Fact]
    public void EscapeTools_AreReportedInReportMode() =>
        new LockdownPolicy(LockdownMode.Report, null, "Student_Desktop_App").Evaluate("taskmgr", false).Should().Be(ProcessDecision.Report);

    [Theory]
    [InlineData("cmd")]
    [InlineData("powershell")]
    public void WindowlessShells_AreNotEscapeTools_SoIdeTerminalsKeepWorking(string name)
    {
        var policy = new LockdownPolicy(LockdownMode.Enforce, new[] { "code.exe" }, "Student_Desktop_App");
        policy.Evaluate(name, hasVisibleWindow: false).Should().Be(ProcessDecision.Allowed);
        policy.Evaluate(name, hasVisibleWindow: true).Should().Be(ProcessDecision.Kill, "a visible console is still an unknown app");
    }

    [Fact]
    public void TeacherCanWhitelistAnEscapeTool() =>
        new LockdownPolicy(LockdownMode.Enforce, new[] { "taskmgr.exe" }, "x").Evaluate("taskmgr", false).Should().Be(ProcessDecision.Allowed);
}
