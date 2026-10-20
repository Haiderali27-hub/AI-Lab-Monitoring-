using System.Windows;
using System.Windows.Interop;

namespace Student_Desktop_App.Services.Lockdown;

/// <summary>
/// Clears the clipboard when the exam starts and whenever another application puts
/// content on it, so nothing can be pasted in from outside. Copy/paste inside the exam
/// window itself keeps working.
/// </summary>
public sealed class ClipboardGuardService : IDisposable
{
    private readonly int _ownPid = Environment.ProcessId;
    private HwndSource? _source;

    /// Raised with the name of the process that placed content on the clipboard.
    public event Action<string>? ExternalClipboardBlocked;

    public void Start(Window window)
    {
        TryClear();
        var handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
        NativeMethods.AddClipboardFormatListener(handle);
    }

    public void Stop()
    {
        if (_source is null) return;
        NativeMethods.RemoveClipboardFormatListener(_source.Handle);
        _source.RemoveHook(WndProc);
        _source = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            var ownerPid = NativeMethods.ProcessIdOf(NativeMethods.GetClipboardOwner());
            if (ownerPid != 0 && ownerPid != _ownPid)
            {
                TryClear();
                ExternalClipboardBlocked?.Invoke(NativeMethods.ProcessNameOf(ownerPid) ?? "unknown");
            }
        }
        return IntPtr.Zero;
    }

    private static void TryClear()
    {
        try { Clipboard.Clear(); }
        catch { /* clipboard briefly locked by another process — next update retries */ }
    }

    public void Dispose() => Stop();
}
