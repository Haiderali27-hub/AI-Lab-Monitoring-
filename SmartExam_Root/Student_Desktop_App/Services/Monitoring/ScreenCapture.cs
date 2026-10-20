using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Student_Desktop_App.Core.Monitoring;

namespace Student_Desktop_App.Services.Monitoring;

/// <summary>
/// Module 5 — captures the whole virtual screen (every monitor, as Windows arranges them) with GDI BitBlt into an
/// off-screen bitmap and hands it to <see cref="JpegEncoder"/>. Thin Win32 wrapper; everything that can be decided
/// without a screen (sizes, schedule, encoding) is in <c>Core/Monitoring</c>. Also used by the Module 2 readiness
/// "Screen capture" check, so the check exercises exactly the path the exam uses.
/// </summary>
public static class ScreenCapture
{
    /// <summary>JPEG of all monitors (≤ 1280 px, q60), or null when the desktop cannot be captured (locked session, policy).</summary>
    public static byte[]? CaptureAllScreensJpeg()
    {
        var source = CaptureVirtualScreen(out _);
        return source is null ? null : JpegEncoder.Encode(source);
    }

    /// <summary>Readiness probe: true when a BitBlt of the virtual screen succeeds (error text otherwise).</summary>
    public static bool CanCapture(out string? error)
    {
        var source = CaptureVirtualScreen(out error);
        return source is not null;
    }

    private static BitmapSource? CaptureVirtualScreen(out string? error)
    {
        error = null;
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN), y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int w = GetSystemMetrics(SM_CXVIRTUALSCREEN), h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (w <= 0 || h <= 0)
        {
            error = "no screen";
            return null;
        }

        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            error = "no screen DC";
            return null;
        }
        var mem = IntPtr.Zero;
        var bmp = IntPtr.Zero;
        try
        {
            mem = CreateCompatibleDC(screen);
            bmp = CreateCompatibleBitmap(screen, w, h);
            if (mem == IntPtr.Zero || bmp == IntPtr.Zero)
            {
                error = "out of GDI resources";
                return null;
            }
            var old = SelectObject(mem, bmp);
            var ok = BitBlt(mem, 0, 0, w, h, screen, x, y, SRCCOPY | CAPTUREBLT);
            var lastError = Marshal.GetLastWin32Error();
            SelectObject(mem, old);
            if (!ok)
            {
                error = $"BitBlt error {lastError}";
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is COMException or ExternalException or OutOfMemoryException)
        {
            error = ex.Message;
            return null;
        }
        finally
        {
            if (bmp != IntPtr.Zero) DeleteObject(bmp);
            if (mem != IntPtr.Zero) DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    // ── Win32 ────────────────────────────────────────────────────────────────
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;   // include layered windows

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
}
