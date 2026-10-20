using Student_Desktop_App.Core.Lockdown;

namespace Student_Desktop_App.Core.Readiness;

/// <summary>One line of the pre-exam checklist. Skipped items count as passed.</summary>
public record ReadinessItemResult(string Name, bool Passed, string Detail, int DurationMs = 0, bool Skipped = false);

/// <summary>What this exam demands of the workstation (from GET /api/exams/student/current).</summary>
public record ReadinessRequirements(bool RequireWebcam, bool RequireMic, bool BlockMultiMonitor, LockdownMode Mode);

/// <summary>
/// Module 2 — pure pass/fail rules for the pre-exam readiness check (no Win32, no HTTP),
/// so every rule is unit-testable. Services/Readiness/ReadinessCheckService gathers the facts.
/// Check names match what the server looks for ("Camera", "Microphone").
/// </summary>
public static class ReadinessPolicy
{
    public const string Network = "Network";
    public const string ClockSkew = "ClockSkew";
    public const string ScreenCapture = "ScreenCapture";
    public const string Camera = "Camera";
    public const string Microphone = "Microphone";
    public const string SecureMode = "SecureMode";
    public const string Disk = "Disk";

    public static readonly string[] AllChecks = { Network, ClockSkew, ScreenCapture, Camera, Microphone, SecureMode, Disk };

    public static readonly TimeSpan MaxLatency = TimeSpan.FromSeconds(2);      // NFR: server reachable < 2 s
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromSeconds(60);
    public const long MinFreeDiskBytes = 200L * 1024 * 1024;                   // local answer backup (Module 6)

    public static string DisplayName(string check) => check switch
    {
        Network => "Network connection",
        ClockSkew => "System clock",
        ScreenCapture => "Screen capture",
        Camera => "Webcam",
        Microphone => "Microphone",
        SecureMode => "Secure mode",
        Disk => "Disk space",
        _ => check
    };

    /// <param name="attempts">Round-trip time of each health probe; null = request failed.</param>
    public static ReadinessItemResult EvaluateNetwork(IReadOnlyList<TimeSpan?> attempts)
    {
        var ok = attempts.Where(a => a.HasValue).Select(a => a!.Value).ToList();
        var total = (int)ok.Sum(a => a.TotalMilliseconds);
        if (ok.Count == 0)
            return new(Network, false, "Exam server unreachable", total);

        var best = ok.Min();
        return best <= MaxLatency
            ? new(Network, true, $"{best.TotalMilliseconds:0} ms", (int)best.TotalMilliseconds)
            : new(Network, false, $"Too slow: {best.TotalMilliseconds:0} ms (limit {MaxLatency.TotalMilliseconds:0} ms)", (int)best.TotalMilliseconds);
    }

    public static ReadinessItemResult EvaluateClockSkew(DateTimeOffset? serverTime, DateTimeOffset localTime)
    {
        if (serverTime is null)
            return new(ClockSkew, false, "Server time unavailable");

        var skew = (localTime - serverTime.Value).Duration();
        return skew <= MaxClockSkew
            ? new(ClockSkew, true, $"{skew.TotalSeconds:0} s difference")
            : new(ClockSkew, false, $"Clock is off by {skew.TotalSeconds:0} s (limit {MaxClockSkew.TotalSeconds:0} s) — fix Windows date/time");
    }

    public static ReadinessItemResult EvaluateScreenCapture(bool captured, string? error) =>
        captured
            ? new(ScreenCapture, true, "Screen can be captured")
            : new(ScreenCapture, false, $"Screen capture failed{(string.IsNullOrWhiteSpace(error) ? "" : ": " + error)}");

    /// <summary>Camera / microphone: only checked when the exam requires the device.</summary>
    public static ReadinessItemResult EvaluateDevice(string check, bool required, int deviceCount, string? deviceName = null)
    {
        if (!required)
            return new(check, true, "Not required for this exam", Skipped: true);
        return deviceCount > 0
            ? new(check, true, deviceName is null ? $"{deviceCount} device(s) found" : deviceName)
            : new(check, false, "No device found");
    }

    public static ReadinessItemResult EvaluateSecureMode(
        ReadinessRequirements req, int monitorCount, bool isRemoteSession, string? virtualMachineVendor, bool keyboardHookOk)
    {
        if (req.Mode == LockdownMode.Off)
            return new(SecureMode, true, "Lockdown off for this exam", Skipped: true);

        var issues = LockdownPolicy.CheckEnvironment(monitorCount, isRemoteSession, req.BlockMultiMonitor)
            .Select(i => i.StartsWith("MultiMonitor") ? $"{monitorCount} monitors connected — disconnect extra screens"
                       : i == "RemoteSession" ? "Running inside a Remote Desktop session"
                       : i)
            .ToList();
        if (virtualMachineVendor is not null) issues.Add($"Running in a virtual machine ({virtualMachineVendor})");
        if (!keyboardHookOk) issues.Add("Keyboard lock could not be installed");

        return issues.Count == 0
            ? new(SecureMode, true, monitorCount <= 1 ? "Single monitor, local session" : $"{monitorCount} monitors (allowed), local session")
            : new(SecureMode, false, string.Join("; ", issues));
    }

    public static ReadinessItemResult EvaluateDisk(long freeBytes)
    {
        var mb = freeBytes / (1024 * 1024);
        var text = mb >= 1024 ? $"{mb / 1024.0:0.0} GB free" : $"{mb} MB free";
        return freeBytes >= MinFreeDiskBytes
            ? new(Disk, true, text)
            : new(Disk, false, $"Only {text} (need {MinFreeDiskBytes / (1024 * 1024)} MB)");
    }

    /// <summary>Vendor name when WMI manufacturer/model identify a hypervisor, else null.</summary>
    public static string? DetectVirtualMachine(string? manufacturer, string? model)
    {
        var text = $"{manufacturer} {model}".ToLowerInvariant();
        if (text.Contains("vmware")) return "VMware";
        if (text.Contains("virtualbox") || text.Contains("innotek")) return "VirtualBox";
        if (text.Contains("qemu") || text.Contains("kvm")) return "QEMU/KVM";
        if (text.Contains("xen")) return "Xen";
        if (text.Contains("parallels")) return "Parallels";
        // Hyper-V guests report "Microsoft Corporation" + "Virtual Machine"
        if (text.Contains("virtual machine")) return "Hyper-V";
        return null;
    }

    /// <summary>Every check present and passed (skipped counts as passed).</summary>
    public static bool PassedAll(IReadOnlyCollection<ReadinessItemResult> items) =>
        items.Count > 0 && items.All(i => i.Passed);
}
