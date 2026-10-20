using System.Diagnostics;
using System.Runtime.InteropServices;
using Student_Desktop_App.Core.Lockdown;

namespace Student_Desktop_App.Services.Lockdown;

/// <summary>
/// System-wide low-level keyboard hook that swallows shell/escape chords (Alt+Tab, Win, Alt+F4,
/// Ctrl+Esc, Ctrl+Shift+Esc, PrintScreen) while the exam runs. Must be installed from a thread
/// with a message loop (the WPF UI thread).
/// </summary>
public sealed class KeyboardHookService : IDisposable
{
    private readonly LockdownPolicy _policy;
    // Kept in a field so the GC never collects the delegate while Windows still calls it.
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _hook = IntPtr.Zero;

    /// Raised (on a thread-pool thread) with a description such as "Alt+Tab".
    public event Action<string>? KeyBlocked;

    public bool IsInstalled => _hook != IntPtr.Zero;

    public KeyboardHookService(LockdownPolicy policy)
    {
        _policy = policy;
        _proc = HookCallback;
    }

    public bool Install()
    {
        if (IsInstalled) return true;
        using var module = Process.GetCurrentProcess().MainModule;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc,
            NativeMethods.GetModuleHandle(module?.ModuleName), 0);
        return IsInstalled;
    }

    public void Uninstall()
    {
        if (!IsInstalled) return;
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == NativeMethods.WM_KEYDOWN || wParam == NativeMethods.WM_SYSKEYDOWN))
        {
            var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var alt = (info.flags & NativeMethods.LLKHF_ALTDOWN) != 0;
            var ctrl = NativeMethods.IsKeyDown(NativeMethods.VK_CONTROL);
            var shift = NativeMethods.IsKeyDown(NativeMethods.VK_SHIFT);

            if (_policy.ShouldBlockKey(info.vkCode, alt, ctrl, shift))
            {
                var description = LockdownPolicy.DescribeKey(info.vkCode, alt, ctrl, shift);
                // Keep the hook callback fast — Windows silently drops slow hooks.
                ThreadPool.QueueUserWorkItem(_ => KeyBlocked?.Invoke(description));
                return 1; // swallow
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
