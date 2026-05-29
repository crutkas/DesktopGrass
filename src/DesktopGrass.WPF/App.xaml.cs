using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using DesktopGrass.WPF.Interop;
using DrawingRectangle = System.Drawing.Rectangle;
using Forms = System.Windows.Forms;

namespace DesktopGrass.WPF;

internal static class Win32App
{
    private static volatile bool s_quitRequested;
    public static void SignalQuit() => s_quitRequested = true;
    public static bool QuitRequested => s_quitRequested;
}

public partial class App : System.Windows.Application
{
    private const ulong AppSeed = 0xD3C7C0F30070D511UL;

    private readonly List<GrassWindow> _windows = new();
    private readonly List<InputEvent> _moveBuffer = new(64);
    private MouseHook? _hook;
    private TrayIcon? _tray;
    private TimeSpan? _lastRenderingTime;
    private bool _shuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Win32.SetProcessDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        CreatePerMonitorWindows();

        _hook = new MouseHook();
        _hook.Install();

        _tray = new TrayIcon(0);
        _tray.Start();

        CompositionTarget.Rendering += OnRendering;
    }

    private void CreatePerMonitorWindows()
    {
        Forms.Screen[] screens = Forms.Screen.AllScreens;
        if (screens.Length == 0 && Forms.Screen.PrimaryScreen is { } primary)
        {
            screens = [primary];
        }

        foreach (Forms.Screen screen in screens)
        {
            DrawingRectangle bounds = screen.WorkingArea;
            double dpiScale = GetDpiScaleForScreen(screen);
            int widthPx = bounds.Width;
            int heightPx = (int)Math.Ceiling((Constants.STRIP_HEIGHT + Constants.HEADROOM) * dpiScale);
            int x = bounds.Left;
            int y = bounds.Bottom - heightPx;
            double monitorWidthDip = widthPx / dpiScale;

            ulong seed = unchecked(AppSeed
                ^ ((ulong)bounds.Left * 0xA0761D6478BD642FUL)
                ^ ((ulong)bounds.Top * 0xE7037ED1A0B428DBUL));

            var grass = new GrassWindow(
                x, y, widthPx, heightPx, dpiScale,
                new DrawingRectangle(bounds.Left, bounds.Top, widthPx, bounds.Height),
                seed, monitorWidthDip);
            grass.Closed += OnGrassWindowClosed;
            _windows.Add(grass);
        }
    }

    private static double GetDpiScaleForScreen(Forms.Screen screen)
    {
        var center = new Win32.POINT
        {
            X = screen.Bounds.Left + (screen.Bounds.Width / 2),
            Y = screen.Bounds.Top + (screen.Bounds.Height / 2),
        };
        IntPtr hmonitor = Win32.MonitorFromPoint(center, Win32.MONITOR_DEFAULTTONEAREST);
        if (hmonitor != IntPtr.Zero
            && Win32.GetDpiForMonitor(hmonitor, Win32.MonitorDpiType.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0
            && dpiX > 0)
        {
            return dpiX / 96.0;
        }

        uint sysDpi = Win32.GetDpiForSystem();
        if (sysDpi == 0)
        {
            sysDpi = 96;
        }
        return sysDpi / 96.0;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (Win32App.QuitRequested)
        {
            RequestShutdown();
            return;
        }

        double dt = 1.0 / 60.0;
        if (e is RenderingEventArgs renderingArgs)
        {
            if (_lastRenderingTime is { } last)
            {
                dt = (renderingArgs.RenderingTime - last).TotalSeconds;
                if (dt <= 0)
                {
                    dt = 1.0 / 60.0;
                }
                if (dt > 1.0 / 30.0)
                {
                    dt = 1.0 / 30.0;
                }
            }
            _lastRenderingTime = renderingArgs.RenderingTime;
        }

        DrainMouseEvents();

        foreach (GrassWindow win in _windows)
        {
            win.Sim.Tick(dt, ReadOnlySpan<InputEvent>.Empty);
            win.Render();
        }
    }

    private void DrainMouseEvents()
    {
        if (_hook is null)
        {
            return;
        }

        while (_hook.Reader.TryRead(out HookEvent hookEvt))
        {
            foreach (GrassWindow win in _windows)
            {
                if (!win.MonitorBoundsPx.Contains(hookEvt.ScreenX, hookEvt.ScreenY))
                {
                    continue;
                }

                double localXdip = (hookEvt.ScreenX - win.MonitorBoundsPx.Left) / win.DpiScale;
                double localYdip = (hookEvt.ScreenY - (win.MonitorBoundsPx.Bottom - win.HeightPx)) / win.DpiScale;

                switch (hookEvt.Kind)
                {
                    case HookEventKind.Move:
                        _moveBuffer.Add(new InputEvent(EventType.Move, localXdip, localYdip, hookEvt.Time));
                        break;
                    case HookEventKind.LeftClick:
                        _moveBuffer.Add(new InputEvent(EventType.Click, localXdip, localYdip, hookEvt.Time));
                        break;
                }

                win.Sim.Tick(0.0, CollectionsMarshal.AsSpan(_moveBuffer));
                _moveBuffer.Clear();
                break;
            }
        }
    }

    private void OnGrassWindowClosed(object? sender, EventArgs e)
    {
        Win32App.SignalQuit();
        RequestShutdown();
    }

    private void RequestShutdown()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        Dispatcher.BeginInvoke((Action)Shutdown);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shuttingDown = true;
        CompositionTarget.Rendering -= OnRendering;
        _hook?.Dispose();
        _tray?.Dispose();
        foreach (GrassWindow window in _windows)
        {
            try { window.Dispose(); } catch { }
        }
        _windows.Clear();
        base.OnExit(e);
    }
}
