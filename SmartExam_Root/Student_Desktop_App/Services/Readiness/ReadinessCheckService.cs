using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using Student_Desktop_App.Core;
using Student_Desktop_App.Core.Readiness;
using Student_Desktop_App.Services.Lockdown;
using Student_Desktop_App.Services.Monitoring;

namespace Student_Desktop_App.Services.Readiness;

/// <summary>
/// Module 2 — runs the pre-exam workstation checks. This class only gathers facts
/// (HTTP probe, GDI capture, WMI, Win32 metrics); every pass/fail rule is in ReadinessPolicy.
/// </summary>
public class ReadinessCheckService(ApiClient apiClient)
{
    private readonly ApiClient _apiClient = apiClient;

    public async Task<List<ReadinessItemResult>> RunAllAsync(ReadinessRequirements req, CancellationToken ct = default)
    {
        var results = new List<ReadinessItemResult>();
        foreach (var check in ReadinessPolicy.AllChecks)
            results.Add(await RunAsync(check, req, ct));
        return results;
    }

    /// <summary>Runs a single check (used by the per-item Retry button).</summary>
    public async Task<ReadinessItemResult> RunAsync(string check, ReadinessRequirements req, CancellationToken ct = default)
    {
        var started = Environment.TickCount64;
        ReadinessItemResult result;
        try
        {
            result = check switch
            {
                ReadinessPolicy.Network => await CheckNetworkAsync(ct),
                ReadinessPolicy.ClockSkew => await CheckClockAsync(ct),
                ReadinessPolicy.ScreenCapture => await Task.Run(CheckScreenCapture, ct),
                ReadinessPolicy.Camera => req.RequireWebcam
                    ? await CheckCameraAsync(ct)
                    : ReadinessPolicy.EvaluateCamera(false, 0, null, null),
                ReadinessPolicy.Microphone => ReadinessPolicy.EvaluateDevice(check, req.RequireMic, req.RequireMic ? (int)waveInGetNumDevs() : 0),
                ReadinessPolicy.SecureMode => await Task.Run(() => ReadinessPolicy.EvaluateSecureMode(req,
                    NativeMethods.GetSystemMetrics(NativeMethods.SM_CMONITORS),
                    NativeMethods.GetSystemMetrics(NativeMethods.SM_REMOTESESSION) != 0,
                    DetectVirtualMachine(),
                    CanInstallKeyboardHook()), ct),
                ReadinessPolicy.Disk => ReadinessPolicy.EvaluateDisk(FreeBytesForBackup()),
                _ => new ReadinessItemResult(check, false, "Unknown check")
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new ReadinessItemResult(check, false, $"Check failed: {ex.Message}");
        }

        return result.DurationMs > 0 ? result : result with { DurationMs = (int)(Environment.TickCount64 - started) };
    }

    private async Task<ReadinessItemResult> CheckNetworkAsync(CancellationToken ct)
    {
        var attempts = new List<TimeSpan?>();
        for (var i = 0; i < 3; i++)
        {
            var probe = await _apiClient.ProbeHealthAsync(ct);
            attempts.Add(probe.Ok ? probe.RoundTrip : null);
            if (probe.Ok && probe.RoundTrip <= ReadinessPolicy.MaxLatency) break;
        }
        return ReadinessPolicy.EvaluateNetwork(attempts);
    }

    private async Task<ReadinessItemResult> CheckClockAsync(CancellationToken ct)
    {
        var probe = await _apiClient.ProbeHealthAsync(ct);
        // Server's Date header is taken mid-request; halve the round trip to compare like for like.
        var local = DateTimeOffset.UtcNow - probe.RoundTrip / 2;
        return ReadinessPolicy.EvaluateClockSkew(probe.Ok ? probe.ServerTime : null, local);
    }

    // Module 2 + 5: a camera must exist AND give a real, uncovered picture. The camera is only opened when WMI finds
    // one, and the probe has its own timeout (some drivers hang on open).
    private static async Task<ReadinessItemResult> CheckCameraAsync(CancellationToken ct)
    {
        var (count, name) = await Task.Run(() =>
        {
            var found = CountCameras(out var first);
            return (found, first);
        }, ct);
        if (count == 0) return ReadinessPolicy.EvaluateCamera(true, 0, null, null);
        // S8: with the face model present the camera must also show exactly one face (the candidate).
        var probe = await WebcamCapture.ProbeAsync(TimeSpan.FromSeconds(8), ct, countFaces: AppConfigProvider.Current.WebcamFaceDetection);
        return ReadinessPolicy.EvaluateCamera(true, count, name, probe);
    }

    private static ReadinessItemResult CheckScreenCapture()
    {
        // All monitors, through exactly the GDI path the exam's screen captures use (Module 5).
        var ok = ScreenCapture.CanCapture(out var error);
        return ReadinessPolicy.EvaluateScreenCapture(ok, error);
    }

    private static int CountCameras(out string? firstName)
    {
        firstName = null;
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name FROM Win32_PnPEntity WHERE (PNPClass = 'Camera' OR PNPClass = 'Image') AND Status = 'OK'");
        var count = 0;
        foreach (var device in searcher.Get())
        {
            firstName ??= device["Name"]?.ToString();
            count++;
        }
        return count;
    }

    /// <summary>Hypervisor vendor from WMI Win32_ComputerSystem, or null on real hardware / WMI failure.</summary>
    public static string? DetectVirtualMachine()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
            foreach (var cs in searcher.Get())
                return ReadinessPolicy.DetectVirtualMachine(cs["Manufacturer"]?.ToString(), cs["Model"]?.ToString());
        }
        catch
        {
            // WMI unavailable — don't fail the student for it
        }
        return null;
    }

    private static readonly NativeMethods.LowLevelKeyboardProc NoopHook = (code, w, l) => NativeMethods.CallNextHookEx(IntPtr.Zero, code, w, l);

    private static bool CanInstallKeyboardHook()
    {
        var hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, NoopHook, NativeMethods.GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) return false;
        NativeMethods.UnhookWindowsHookEx(hook);
        return true;
    }

    private static long FreeBytesForBackup()
    {
        var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) ?? "C:\\";
        return new DriveInfo(root).AvailableFreeSpace;
    }

    // ── Win32 ────────────────────────────────────────────────────────────────
    [DllImport("winmm.dll")] private static extern uint waveInGetNumDevs();
}
