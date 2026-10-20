using System.IO;

namespace Student_Desktop_App.Core.Lockdown;

/// <summary>How strictly unauthorized applications are handled during an exam.</summary>
public enum LockdownMode
{
    /// No lockdown at all (development only).
    Off,
    /// Keys/window locked; unauthorized apps are reported but left running.
    Report,
    /// As Report, plus unauthorized apps are terminated.
    Enforce
}

public enum ProcessDecision
{
    Allowed,
    Report,
    Kill
}

/// <summary>
/// Pure lockdown decisions (no Win32), so every rule is unit-testable.
/// The Win32 services in Services/Lockdown only gather facts and act on these answers.
/// </summary>
public class LockdownPolicy
{
    // Virtual-key codes (winuser.h)
    public const int VK_TAB = 0x09;
    public const int VK_ESCAPE = 0x1B;
    public const int VK_F4 = 0x73;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;
    public const int VK_APPS = 0x5D;
    public const int VK_SNAPSHOT = 0x2C;

    // Always tolerated: Windows shell/session processes and anything that owns no window.
    private static readonly HashSet<string> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "csrss", "winlogon", "svchost", "sihost", "ctfmon", "conhost",
        "searchhost", "startmenuexperiencehost", "shellexperiencehost", "textinputhost",
        "applicationframehost", "systemsettings", "lockapp", "runtimebroker", "taskhostw",
        "securityhealthsystray", "dllhost", "fontdrvhost", "audiodg", "smartscreen"
    };

    // Remote-control / recording / messaging tools that are never acceptable, window or not.
    private static readonly HashSet<string> AlwaysBlocked = new(StringComparer.OrdinalIgnoreCase)
    {
        "teamviewer", "anydesk", "rustdesk", "mstsc", "obs64", "obs32", "discord",
        "whatsapp", "telegram", "slack", "zoom", "ms-teams", "chrome", "msedge", "firefox", "opera", "brave"
    };

    private readonly HashSet<string> _allowedApps;

    public LockdownMode Mode { get; }
    public string OwnProcessName { get; }

    public LockdownPolicy(LockdownMode mode, IEnumerable<string>? allowedApps, string ownProcessName)
    {
        Mode = mode;
        OwnProcessName = Normalize(ownProcessName);
        _allowedApps = new HashSet<string>(
            (allowedApps ?? Enumerable.Empty<string>()).Select(Normalize).Where(a => a.Length > 0),
            StringComparer.OrdinalIgnoreCase);
    }

    /// "Code.EXE", "C:\\x\\code.exe", " code " → "code"
    public static string Normalize(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return string.Empty;
        var name = Path.GetFileName(processName.Trim());
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    public bool IsAllowedApp(string processName)
    {
        var name = Normalize(processName);
        return name.Equals(OwnProcessName, StringComparison.OrdinalIgnoreCase) || _allowedApps.Contains(name);
    }

    /// <summary>
    /// Should this key press be swallowed? Only shell/escape chords are blocked —
    /// ordinary typing and in-app shortcuts (Ctrl+C inside the editor, arrows, Tab) pass.
    /// Ctrl+Alt+Del is handled by Windows itself and can never be intercepted.
    /// </summary>
    public bool ShouldBlockKey(int vkCode, bool alt, bool ctrl, bool shift)
    {
        if (Mode == LockdownMode.Off) return false;

        return vkCode switch
        {
            VK_LWIN or VK_RWIN or VK_APPS or VK_SNAPSHOT => true,
            VK_TAB when alt => true,                       // Alt+Tab, Alt+Shift+Tab
            VK_ESCAPE when alt || ctrl => true,            // Alt+Esc, Ctrl+Esc, Ctrl+Shift+Esc
            VK_F4 when alt => true,                        // Alt+F4
            _ => false
        };
    }

    public static string DescribeKey(int vkCode, bool alt, bool ctrl, bool shift)
    {
        var key = vkCode switch
        {
            VK_LWIN or VK_RWIN => "Win",
            VK_APPS => "Menu",
            VK_SNAPSHOT => "PrintScreen",
            VK_TAB => "Tab",
            VK_ESCAPE => "Esc",
            VK_F4 => "F4",
            _ => $"0x{vkCode:X2}"
        };
        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        parts.Add(key);
        return string.Join('+', parts);
    }

    /// <summary>
    /// Decide what to do with a running process. Only processes that show a window in the
    /// student's session are considered user apps; background services are ignored unless
    /// they are on the always-blocked list (remote control, recording, messaging, browsers).
    /// </summary>
    public ProcessDecision Evaluate(string processName, bool hasVisibleWindow)
    {
        if (Mode == LockdownMode.Off) return ProcessDecision.Allowed;

        var name = Normalize(processName);
        if (name.Length == 0 || IsAllowedApp(name)) return ProcessDecision.Allowed;

        var suspicious = AlwaysBlocked.Contains(name) || (hasVisibleWindow && !SystemProcesses.Contains(name));
        if (!suspicious) return ProcessDecision.Allowed;

        return Mode == LockdownMode.Enforce ? ProcessDecision.Kill : ProcessDecision.Report;
    }

    /// <summary>
    /// Is the foreground window acceptable? Decided by the owning process ID, never the
    /// window title (a Notepad window titled "Exam" must not count as the exam).
    /// </summary>
    public bool IsForegroundAcceptable(int foregroundPid, int ownPid, string? foregroundProcessName)
    {
        if (foregroundPid == 0) return true;               // desktop switching / no window: let focus logic decide
        if (foregroundPid == ownPid) return true;
        return foregroundProcessName is not null && _allowedApps.Contains(Normalize(foregroundProcessName));
    }

    /// <summary>Environment problems that make the workstation unfit for a proctored exam.</summary>
    public static IReadOnlyList<string> CheckEnvironment(int monitorCount, bool isRemoteSession, bool blockMultiMonitor)
    {
        var issues = new List<string>();
        if (blockMultiMonitor && monitorCount > 1) issues.Add($"MultiMonitor:{monitorCount}");
        if (isRemoteSession) issues.Add("RemoteSession");
        return issues;
    }
}
