using FluentAssertions;
using Student_Desktop_App.Core.Lockdown;
using static Student_Desktop_App.Core.Lockdown.LockdownPolicy;

namespace Student_Desktop_App.Tests.Lockdown;

/// Catalog M4-U-01 … M4-U-12 (Docs/TEST_CASES.md).
public class LockdownPolicyTests
{
    private const int VK_A = 0x41;
    private const int VK_C = 0x43;
    private const int VK_LEFT = 0x25;

    private static LockdownPolicy Policy(LockdownMode mode = LockdownMode.Enforce, params string[] allowed) =>
        new(mode, allowed, "Student_Desktop_App");

    [Fact]
    public void M4_U_01_AltTab_IsBlocked()
    {
        Policy().ShouldBlockKey(VK_TAB, alt: true, ctrl: false, shift: false).Should().BeTrue();
        Policy().ShouldBlockKey(VK_TAB, alt: true, ctrl: false, shift: true).Should().BeTrue();
    }

    [Fact]
    public void M4_U_02_WindowsKeys_AreBlocked()
    {
        Policy().ShouldBlockKey(VK_LWIN, false, false, false).Should().BeTrue();
        Policy().ShouldBlockKey(VK_RWIN, false, false, false).Should().BeTrue();
        Policy().ShouldBlockKey(VK_APPS, false, false, false).Should().BeTrue();
    }

    [Fact]
    public void M4_U_03_AltF4_IsBlocked() =>
        Policy().ShouldBlockKey(VK_F4, alt: true, ctrl: false, shift: false).Should().BeTrue();

    [Fact]
    public void M4_U_04_CtrlEsc_And_CtrlShiftEsc_AreBlocked()
    {
        Policy().ShouldBlockKey(VK_ESCAPE, alt: false, ctrl: true, shift: false).Should().BeTrue();
        Policy().ShouldBlockKey(VK_ESCAPE, alt: false, ctrl: true, shift: true).Should().BeTrue();
        Policy().ShouldBlockKey(VK_ESCAPE, alt: true, ctrl: false, shift: false).Should().BeTrue();
    }

    [Fact]
    public void M4_U_05_PrintScreen_IsBlocked() =>
        Policy().ShouldBlockKey(VK_SNAPSHOT, false, false, false).Should().BeTrue();

    [Fact]
    public void M4_U_06_NormalTyping_And_InAppShortcuts_Pass()
    {
        var p = Policy();
        p.ShouldBlockKey(VK_A, false, false, false).Should().BeFalse();
        p.ShouldBlockKey(VK_A, false, false, true).Should().BeFalse();
        p.ShouldBlockKey(VK_C, false, ctrl: true, shift: false).Should().BeFalse();
        p.ShouldBlockKey(VK_LEFT, false, false, false).Should().BeFalse();
        p.ShouldBlockKey(VK_TAB, false, false, false).Should().BeFalse();   // indenting code
        p.ShouldBlockKey(VK_ESCAPE, false, false, false).Should().BeFalse();
        p.ShouldBlockKey(VK_F4, false, false, false).Should().BeFalse();
    }

    [Fact]
    public void M4_U_06b_OffMode_BlocksNothing() =>
        Policy(LockdownMode.Off).ShouldBlockKey(VK_LWIN, false, false, false).Should().BeFalse();

    [Fact]
    public void M4_U_07_AllowedApp_IsAllowed() =>
        Policy(LockdownMode.Enforce, "code.exe").Evaluate("code", hasVisibleWindow: true).Should().Be(ProcessDecision.Allowed);

    [Theory]
    [InlineData("explorer")]
    [InlineData("csrss")]
    [InlineData("dwm")]
    [InlineData("Student_Desktop_App")]
    public void M4_U_08_SystemAndOwnProcesses_AreAllowed(string name) =>
        Policy().Evaluate(name, hasVisibleWindow: true).Should().Be(ProcessDecision.Allowed);

    [Fact]
    public void M4_U_08b_BackgroundProcessWithoutWindow_IsIgnored() =>
        Policy().Evaluate("SomeUpdaterService", hasVisibleWindow: false).Should().Be(ProcessDecision.Allowed);

    [Fact]
    public void M4_U_09_UnknownWindowedApp_InEnforceMode_IsKilled()
    {
        Policy(LockdownMode.Enforce).Evaluate("chrome", hasVisibleWindow: true).Should().Be(ProcessDecision.Kill);
        Policy(LockdownMode.Enforce).Evaluate("notepad", hasVisibleWindow: true).Should().Be(ProcessDecision.Kill);
    }

    [Fact]
    public void M4_U_09b_RemoteControlTool_IsCaughtEvenWithoutWindow() =>
        Policy(LockdownMode.Enforce).Evaluate("anydesk", hasVisibleWindow: false).Should().Be(ProcessDecision.Kill);

    [Fact]
    public void M4_U_10_UnknownApp_InReportMode_IsOnlyReported() =>
        Policy(LockdownMode.Report).Evaluate("chrome", hasVisibleWindow: true).Should().Be(ProcessDecision.Report);

    [Fact]
    public void M4_U_10b_TeacherCanWhitelistABrowser() =>
        Policy(LockdownMode.Enforce, "chrome.exe").Evaluate("chrome", true).Should().Be(ProcessDecision.Allowed);

    [Theory]
    [InlineData("Code.EXE")]
    [InlineData("code")]
    [InlineData(@"C:\Program Files\VS Code\code.exe")]
    [InlineData("  CODE.exe ")]
    public void M4_U_11_AllowedAppMatching_IgnoresCaseExtensionAndPath(string configured) =>
        Policy(LockdownMode.Enforce, configured).Evaluate("code", true).Should().Be(ProcessDecision.Allowed);

    [Fact]
    public void M4_U_12_Foreground_IsJudgedByProcessId_NotTitle()
    {
        var p = Policy(LockdownMode.Enforce, "code.exe");
        const int own = 100;

        p.IsForegroundAcceptable(foregroundPid: own, ownPid: own, foregroundProcessName: "Student_Desktop_App").Should().BeTrue();
        p.IsForegroundAcceptable(200, own, "code").Should().BeTrue();
        // A Notepad window someone titled "SmartExam — Exam" is still Notepad.
        p.IsForegroundAcceptable(300, own, "notepad").Should().BeFalse();
        p.IsForegroundAcceptable(300, own, null).Should().BeFalse();
    }

    [Fact]
    public void Environment_MultiMonitorAndRemoteSession_AreReported()
    {
        CheckEnvironment(1, false, blockMultiMonitor: true).Should().BeEmpty();
        CheckEnvironment(2, false, blockMultiMonitor: true).Should().ContainSingle().Which.Should().Be("MultiMonitor:2");
        CheckEnvironment(2, false, blockMultiMonitor: false).Should().BeEmpty();
        CheckEnvironment(1, true, blockMultiMonitor: true).Should().Contain("RemoteSession");
    }

    [Fact]
    public void DescribeKey_IsReadableForProctors() =>
        DescribeKey(VK_ESCAPE, alt: false, ctrl: true, shift: true).Should().Be("Ctrl+Shift+Esc");
}
