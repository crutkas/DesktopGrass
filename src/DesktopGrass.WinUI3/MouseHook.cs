// MouseHook.cs
//
// Global low-level mouse hook (WH_MOUSE_LL). Same pattern as the Native
// and Win2D tracks:
//   * Hook is installed on the UI thread via SetWindowsHookExW.
//   * The callback delegate is *pinned* (held in an instance field) so the
//     GC can't relocate it. Forgetting this gives you a sporadic
//     AccessViolation on the first GC after install — historically the #1
//     foot-gun in LL hook code.
//   * Callback does minimal work: extract x/y/type, push onto a caller-
//     provided sink, ALWAYS return CallNextHookEx(...) so we never consume
//     input. The hook is observe-only by design (plan §"Input observation").

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DesktopGrass.WinUI3.Interop;

namespace DesktopGrass.WinUI3;

internal sealed class MouseHook : IDisposable
{
    private readonly Action<InputEvent> _sink;
    private readonly User32.LowLevelMouseProc _proc; // PINNED — see ctor.
    private IntPtr _hook;

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public MouseHook(Action<InputEvent> sink)
    {
        _sink = sink;
        // Hold the delegate in a field so the GC keeps it alive for the
        // lifetime of the hook. Don't construct it inline at SetWindowsHookExW.
        _proc = HookProc;
    }

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        // WH_MOUSE_LL is a process-global hook. hMod must be a real module
        // handle (the docs say a DLL handle, but for managed callers the
        // host EXE module is accepted).
        var module = User32.GetModuleHandleW(null);
        _hook = User32.SetWindowsHookExW(HookConstants.WH_MOUSE_LL, _proc, module, 0u);
        if (_hook == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"SetWindowsHookExW(WH_MOUSE_LL) failed; Win32 error {err}");
        }
    }

    // For the smoke test: query whether the hook is currently installed.
    public bool IsInstalled => _hook != IntPtr.Zero;

    public void Uninstall()
    {
        if (_hook == IntPtr.Zero) return;
        User32.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    public void Dispose() => Uninstall();

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // nCode < 0 → must just chain. ALWAYS chain at the end too — we
        // are observe-only.
        if (nCode < 0) return User32.CallNextHookEx(_hook, nCode, wParam, lParam);

        try
        {
            int msg = (int)(long)wParam;
            switch (msg)
            {
                case HookConstants.WM_MOUSEMOVE:
                case HookConstants.WM_LBUTTONDOWN:
                {
                    var data = Marshal.PtrToStructure<User32.MSLLHOOKSTRUCT>(lParam);
                    var type = msg == HookConstants.WM_LBUTTONDOWN
                        ? InputEventType.Click
                        : InputEventType.Move;
                    double t = _clock.Elapsed.TotalSeconds;
                    _sink(new InputEvent(type, data.pt.X, data.pt.Y, t));
                    break;
                }
            }
        }
        catch
        {
            // Swallow — we are in a hook callback on a system thread.
            // Throwing here uninstalls the hook and destabilises every
            // process on the desktop. Logging would re-enter and is also
            // a bad idea.
        }

        return User32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
