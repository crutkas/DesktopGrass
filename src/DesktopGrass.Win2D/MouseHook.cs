// MouseHook.cs - WH_MOUSE_LL global hook + bounded SPSC event channel.
//
// The low-level mouse hook runs synchronously on the hook thread (the
// thread that installed the hook, in our case the main UI thread). The
// callback must:
//   * Always return CallNextHookEx so we never consume input.
//   * Do minimal work — queue an event and return.
//
// We use System.Threading.Channels with a bounded capacity and DropOldest
// full-mode so a runaway producer (rapid cursor motion) can't bloat memory.
// The renderer drains the channel synchronously each frame.

using System;
using System.Threading.Channels;
using DesktopGrass.Win2D.Interop;

namespace DesktopGrass.Win2D;

internal enum HookEventKind { Move, LeftClick }

internal readonly struct HookEvent
{
    public readonly HookEventKind Kind;
    public readonly int ScreenX;
    public readonly int ScreenY;
    public readonly double Time;

    public HookEvent(HookEventKind kind, int x, int y, double time)
    {
        Kind = kind; ScreenX = x; ScreenY = y; Time = time;
    }
}

internal sealed class MouseHook : IDisposable
{
    // The delegate MUST be a static field (or instance field rooted by a
    // long-lived owner) so the GC doesn't collect it while the hook is
    // installed. A captured local would be reclaimed and the hook would
    // crash on the next mouse event.
    private readonly Win32.HookProc _proc;
    private IntPtr _hook;
    private readonly Channel<HookEvent> _channel;
    private readonly long _qpcFreq;

    public ChannelReader<HookEvent> Reader => _channel.Reader;

    public MouseHook()
    {
        _channel = Channel.CreateBounded<HookEvent>(new BoundedChannelOptions(capacity: 1024)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        _proc = HookCallback;
        Win32.QueryPerformanceFrequency(out _qpcFreq);
    }

    public void Install()
    {
        // hMod=0 with a managed callback is documented as supported when
        // the hook is installed for the calling thread's process only via
        // LowLevelMouseProc; in practice WH_MOUSE_LL requires the module
        // handle of the calling process so its code is callable by the
        // hook thread. Passing GetModuleHandle(null) is the standard idiom.
        var hMod = Win32.GetModuleHandleW(null);
        _hook = Win32.SetWindowsHookExW(Win32.WH_MOUSE_LL, _proc, hMod, 0);
        if (_hook == IntPtr.Zero)
        {
            int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SetWindowsHookEx WH_MOUSE_LL failed: error {err}");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            uint msg = (uint)wParam.ToInt32();
            var data = System.Runtime.InteropServices.Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
            double t = NowSeconds();
            switch (msg)
            {
                case Win32.WM_MOUSEMOVE:
                    _channel.Writer.TryWrite(new HookEvent(HookEventKind.Move, data.Pt.X, data.Pt.Y, t));
                    break;
                case Win32.WM_LBUTTONDOWN:
                    _channel.Writer.TryWrite(new HookEvent(HookEventKind.LeftClick, data.Pt.X, data.Pt.Y, t));
                    break;
            }
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private double NowSeconds()
    {
        Win32.QueryPerformanceCounter(out long now);
        return (double)now / _qpcFreq;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _channel.Writer.TryComplete();
    }
}
