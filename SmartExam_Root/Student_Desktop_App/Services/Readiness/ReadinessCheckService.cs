using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using Student_Desktop_App.Core.Readiness;
using Student_Desktop_App.Services.Lockdown;

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
                    ? await Task.Run(() => ReadinessPolicy.EvaluateDevice(check, true, CountCameras(out var name), name), ct)
                    : ReadinessPolicy.EvaluateDevice(check, false, 0),
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

    private static ReadinessItemResult CheckScreenCapture()
    {
        // Copy the primary screen into an off-screen bitmap — the same GDI path a proctoring
        // screenshot uses; fails on locked sessions / capture-blocking policies.
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return ReadinessPolicy.EvaluateScreenCapture(false, "no screen DC");
        var mem = IntPtr.Zero;
        var bmp = IntPtr.Zero;
        try
        {
            int w = GetSystemMetrics(SM_CXSCREEN), h = GetSystemMetrics(SM_CYSCREEN);
            mem = CreateCompatibleDC(screen);
            bmp = CreateCompatibleBitmap(screen, w, h);
            if (mem == IntPtr.Zero || bmp == IntPtr.Zero) return ReadinessPolicy.EvaluateScreenCapture(false, "out of GDI resources");
            var old = SelectObject(mem, bmp);
            var ok = BitBlt(mem, 0, 0, w, h, screen, 0, 0, SRCCOPY);
            SelectObject(mem, old);
            return ReadinessPolicy.EvaluateScreenCapture(ok, ok ? null : $"BitBlt error {Marshal.GetLastWin32Error()}");
        }
        finally
        {
            if (bmp != IntPtr.Zero) DeleteObject(bmp);
            if (mem != IntPtr.Zero) DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
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
    private const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;
    private const int SRCCOPY = 0x00CC0020;

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("winmm.dll")] private static extern uint waveInGetNumDevs();
}
