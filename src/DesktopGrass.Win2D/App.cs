// App.cs - lifecycle orchestration: per-monitor window enumeration, mouse
// hook installation, tray, and the timing loop that drives renders + sim
// ticks. The main thread runs PeekMessage; the render is interleaved as
// "free time" between message dispatches.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using DesktopGrass.Win2D.Interop;

namespace DesktopGrass.Win2D;

internal static class Win32App
{
    private static volatile bool s_quitRequested;
    public static void SignalQuit() => s_quitRequested = true;
    public static bool QuitRequested => s_quitRequested;

    // -1 = no pending change; 0..2 = pending Scene to apply.
    private static int s_pendingScene = -1;
    public static void RequestSceneChange(Scene s) =>
        System.Threading.Interlocked.Exchange(ref s_pendingScene, (int)s);
    public static int ConsumePendingSceneChange() =>
        System.Threading.Interlocked.Exchange(ref s_pendingScene, -1);
}

internal sealed class App : IDisposable
{
    private const string WindowClassName = "DesktopGrass.Win2D.Window";
    private const ulong AppSeed = 0xD3C7C0F30070D511UL; // arbitrary launch seed

    private readonly List<GrassWindow> _windows = new();
    private MouseHook? _hook;
    private TrayIcon? _tray;
    private Scene _currentScene = Constants.SCENE_DEFAULT;
    private Win32.WndProc? _wndProcDelegate; // keep alive for class lifetime
    private ushort _classAtom;
    private long _qpcFreq;

    public void Run()
    {
        Win32.QueryPerformanceFrequency(out _qpcFreq);

        // Per-Monitor V2 DPI awareness. Best-effort; if manifest already
        // declared it this is a no-op.
        Win32.SetProcessDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        RegisterWindowClass();
        CreatePerMonitorWindows();

        _hook = new MouseHook();
        _hook.Install();

        _tray = new TrayIcon(0, _currentScene);
        _tray.Start();

        RunMessageLoop();

        Shutdown();
    }

    private void RegisterWindowClass()
    {
        _wndProcDelegate = WndProc;
        IntPtr fp = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        IntPtr hInstance = Win32.GetModuleHandleW(null);
        IntPtr classNamePtr = Marshal.StringToHGlobalUni(WindowClassName);

        var wc = new Win32.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEXW>(),
            style = Win32.CS_HREDRAW | Win32.CS_VREDRAW,
            lpfnWndProc = fp,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = hInstance,
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = IntPtr.Zero,
            lpszClassName = classNamePtr,
            hIconSm = IntPtr.Zero,
        };
        _classAtom = Win32.RegisterClassExW(wc);
        if (_classAtom == 0)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"RegisterClassExW failed for '{WindowClassName}': error {err}");
        }
        // classNamePtr is intentionally leaked: it must outlive the class.
    }

    private void CreatePerMonitorWindows()
    {
        var monitors = new List<(IntPtr hMonitor, Win32.RECT bounds, uint dpi)>();
        Win32.MonitorEnumProc cb = (IntPtr hMonitor, IntPtr hdc, ref Win32.RECT rc, IntPtr data) =>
        {
            // Get monitor info for accurate DPI handling.
            var mi = new Win32.MONITORINFO { cbSize = (uint)Marshal.SizeOf<Win32.MONITORINFO>() };
            Win32.GetMonitorInfoW(hMonitor, ref mi);
            // Use the work area, not the full monitor rect, so the grass sits
            // on top of the taskbar instead of being drawn behind it.
            monitors.Add((hMonitor, mi.rcWork, 96));
            return true;
        };
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);

        // For each monitor: window full width, anchored to bottom.
        foreach (var (_, bounds, _) in monitors)
        {
            int widthPx = bounds.Width;
            int heightDip = (int)Math.Ceiling(Constants.STRIP_HEIGHT + Constants.HEADROOM);
            // We use System DPI for sizing; per-window DPI is read post-create.
            uint sysDpi = Win32.GetDpiForSystem();
            float scale = sysDpi / 96f;
            int heightPx = (int)Math.Ceiling(heightDip * scale);

            int x = bounds.Left;
            int y = bounds.Bottom - heightPx;

            var hInstance = Win32.GetModuleHandleW(null);
            IntPtr hwnd = Win32.CreateWindowExW(
                Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_TOPMOST
                    | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE
                    | Win32.WS_EX_NOREDIRECTIONBITMAP,
                WindowClassName,
                "DesktopGrass.Win2D",
                Win32.WS_POPUP,
                x, y, widthPx, heightPx,
                IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

            if (hwnd == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"CreateWindowExW failed: error {err}");
            }

            // Read actual per-monitor DPI (PMv2).
            uint winDpi = Win32.GetDpiForWindow(hwnd);
            if (winDpi == 0) winDpi = sysDpi;
            float perMonScale = winDpi / 96f;

            // If per-monitor scale differs from sysScale, recompute heightPx
            // (rare on single-monitor; correct on mixed-DPI setups).
            if (Math.Abs(perMonScale - scale) > 1e-3)
            {
                heightPx = (int)Math.Ceiling(heightDip * perMonScale);
                Win32.SetWindowPos(hwnd, IntPtr.Zero,
                    x, bounds.Bottom - heightPx, widthPx, heightPx,
                    Win32.SWP_NOACTIVATE);
            }

            double monitorWidthDip = widthPx / perMonScale;

            // Per-monitor seed: combine global seed with monitor origin so
            // different screens get different blade layouts.
            ulong seed = unchecked(AppSeed
                ^ ((ulong)bounds.Left * 0xA0761D6478BD642FUL)
                ^ ((ulong)bounds.Top * 0xE7037ED1A0B428DBUL));

            var grass = new GrassWindow(
                hwnd, widthPx, heightPx, perMonScale,
                new Rectangle(bounds.Left, bounds.Top, widthPx, bounds.Height),
                seed, monitorWidthDip);

            // Show without activating. Use NOACTIVATE-friendly path.
            Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST,
                0, 0, 0, 0,
                Win32.SWP_NOACTIVATE | Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_SHOWWINDOW);

            _windows.Add(grass);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam)
    {
        switch (msg)
        {
            case Win32.WM_NCHITTEST:
                // Defensive — WS_EX_TRANSPARENT already does this, but be
                // explicit so the click-through invariant survives any
                // accidental future style change.
                return (IntPtr)Win32.HTTRANSPARENT;

            case Win32.WM_DPICHANGED:
                // For v1, ignore: monitors get rebuilt on WM_DISPLAYCHANGE
                // which covers the typical resolution/DPI change scenario.
                return IntPtr.Zero;

            case Win32.WM_DISPLAYCHANGE:
                Win32App.SignalQuit(); // simplest: rebuild on next launch
                return IntPtr.Zero;

            case Win32.WM_CLOSE:
                Win32App.SignalQuit();
                return IntPtr.Zero;

            case Win32.WM_DESTROY:
                Win32.PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return Win32.DefWindowProcW(hwnd, msg, wparam, lparam);
    }

    private void RunMessageLoop()
    {
        long lastTick;
        Win32.QueryPerformanceCounter(out lastTick);
        const double TargetFrameSec = 1.0 / 60.0;

        var moveBuffer = new List<InputEvent>(64);

        while (!Win32App.QuitRequested)
        {
            while (Win32.PeekMessageW(out var msg, IntPtr.Zero, 0, 0, Win32.PM_REMOVE))
            {
                if (msg.Message == Win32.WM_QUIT)
                {
                    Win32App.SignalQuit();
                    break;
                }
                Win32.TranslateMessage(in msg);
                Win32.DispatchMessageW(in msg);
            }
            if (Win32App.QuitRequested) break;

            Win32.QueryPerformanceCounter(out long now);
            double dt = (now - lastTick) / (double)_qpcFreq;
            if (dt < TargetFrameSec)
            {
                Thread.Sleep(1);
                continue;
            }
            // Cap dt for stability (see §10).
            if (dt > 1.0 / 30.0) dt = 1.0 / 30.0;
            lastTick = now;

            // Drain hook events and route per-window.
            if (_hook is not null)
            {
                while (_hook.Reader.TryRead(out var hookEvt))
                {
                    foreach (var win in _windows)
                    {
                        if (!win.MonitorBoundsPx.Contains(hookEvt.ScreenX, hookEvt.ScreenY)) continue;

                        // Convert screen px to window-local DIPs.
                        double localXdip = (hookEvt.ScreenX - win.MonitorBoundsPx.Left) / win.DpiScale;
                        double localYdip = (hookEvt.ScreenY - (win.MonitorBoundsPx.Bottom - win.HeightPx)) / win.DpiScale;

                        switch (hookEvt.Kind)
                        {
                            case HookEventKind.Move:
                                moveBuffer.Add(new InputEvent(EventType.Move, localXdip, localYdip, hookEvt.Time));
                                break;
                            case HookEventKind.LeftClick:
                                moveBuffer.Add(new InputEvent(EventType.Click, localXdip, localYdip, hookEvt.Time));
                                break;
                        }

                        // Apply this window's slice now and clear buffer.
                        win.Sim.Tick(0.0, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(moveBuffer));
                        moveBuffer.Clear();
                        break;
                    }
                }
            }

            int pending = Win32App.ConsumePendingSceneChange();
            if (pending >= 0)
            {
                Scene s = (Scene)pending;
                if (s != _currentScene)
                {
                    _currentScene = s;
                    foreach (var w in _windows) w.SetScene(s);
                }
            }

            foreach (var win in _windows)
            {
                win.Sim.Tick(dt, ReadOnlySpan<InputEvent>.Empty);
                win.Render();
            }
        }
    }

    private void Shutdown()
    {
        _hook?.Dispose();
        _tray?.Dispose();
        foreach (var w in _windows)
        {
            try { w.Dispose(); } catch { }
        }
        _windows.Clear();
        if (_classAtom != 0)
        {
            Win32.UnregisterClassW(WindowClassName, Win32.GetModuleHandleW(null));
        }
    }

    public void Dispose() => Shutdown();
}
