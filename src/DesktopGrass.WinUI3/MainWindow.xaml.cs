// MainWindow.xaml.cs
//
// One MainWindow per monitor. Owns:
//   * The Sim (pure logic from Sim.cs)
//   * The GrassRenderer (Composition-based; lives in GrassRenderer.cs)
//   * The per-window dispatcher timer driving Tick + render at 60 Hz
//   * A bounded mailbox of InputEvents fed by the global mouse hook
//
// The window itself is a transparent, click-through, topmost popup, set
// up by WindowAttacher right after Activate().

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using WinRT.Interop;

namespace DesktopGrass.WinUI3;

public sealed partial class MainWindow : Window
{
    private readonly MonitorBounds _monitor;
    private readonly Sim _sim;
    private readonly GrassRenderer _renderer;
    private readonly DispatcherTimer _timer;

    // Mailbox from the LL mouse hook thread → UI thread.
    private readonly ConcurrentQueue<InputEvent> _inbox = new();

    private readonly Stopwatch _wallClock = Stopwatch.StartNew();
    private TimeSpan _lastTick = TimeSpan.Zero;

    internal MainWindow(MonitorBounds monitor)
    {
        _monitor = monitor;
        InitializeComponent();

        // The window's contents are pure transparency until we attach the
        // Composition visual tree. SystemBackdrop stays null for that.
        this.SystemBackdrop = null;

        // Title is the smoke-harness contract: the harness can't match the
        // WinUI 3 window class (WinUIDesktopWin32WindowClass, owned by the
        // framework), so it matches by title regex instead.
        this.Title = "DesktopGrass.WinUI3.Window";

        // Build the Sim now so it exists before the first frame.
        double windowHeightDip = Constants.StripHeight + Constants.Headroom;
        _sim = new Sim(windowHeightDip);
        _sim.Generate(Constants.CanonicalTestSeed, monitor.Width, Constants.DefaultDensity);

        // Hand the root grid to the renderer; it attaches a Composition
        // visual tree underneath.
        _renderer = new GrassRenderer(RootGrid, _sim);

        // 60 Hz tick. DispatcherTimer fires on the UI thread, which is
        // where Composition wants its updates.
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1000.0 / 60.0),
        };
        _timer.Tick += OnTick;
        _timer.Start();

        // After WinUI 3 has constructed its HWND, retroactively punch the
        // click-through ExStyles on. We have to do this both right now AND
        // after the first Activate() because the framework keeps re-asserting
        // its own style during the activation sequence on some builds.
        this.Activated += OnFirstActivated;
        ApplyClickThroughAndPosition();
    }

    public IntPtr Hwnd => WindowNative.GetWindowHandle(this);

    internal void EnqueueInputEvent(InputEvent ev)
    {
        // Cheap, lock-free. The UI thread drains the queue once per tick.
        _inbox.Enqueue(ev);
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        // Apply ExStyles defensively on the first activation in case WinUI
        // reset them. Then unsubscribe — we don't want to fight the user
        // every time the window gains focus.
        ApplyClickThroughAndPosition();
        this.Activated -= OnFirstActivated;
    }

    private void ApplyClickThroughAndPosition()
    {
        var hwnd = Hwnd;
        WindowAttacher.PositionAtMonitorBottom(
            hwnd,
            _monitor.X, _monitor.Y, _monitor.Width, _monitor.Height,
            (int)Constants.StripHeight, (int)Constants.Headroom);
        WindowAttacher.MakeClickThroughTopmost(hwnd);
    }

    private void OnTick(object? sender, object e)
    {
        // dt in seconds since previous tick; first tick gets a sane default.
        var now = _wallClock.Elapsed;
        double dt = _lastTick == TimeSpan.Zero
            ? 1.0 / 60.0
            : Math.Max((now - _lastTick).TotalSeconds, 1.0 / 1000.0);
        if (dt > 1.0 / 30.0) dt = 1.0 / 30.0; // spec §10: never feed a multi-second dt
        _lastTick = now;

        // Drain the mailbox into a local span (Sim wants ReadOnlySpan).
        var events = DrainInbox();
        _sim.Tick(dt, events);
        _renderer.Render();
    }

    private InputEvent[] DrainInbox()
    {
        // 1024 is generous: at 1000 Hz hook + 60 Hz tick the max is 17,
        // and we'd be in trouble well before 1024.
        var buf = new InputEvent[1024];
        int n = 0;
        while (n < buf.Length && _inbox.TryDequeue(out var ev))
        {
            // Convert screen coords → window-local DIPs.
            // For v1 we trust DIP == physical pixel (no DPI scaling beyond
            // PerMonitorV2 default). Real impl would call MapWindowPoints.
            double localX = ev.X - _monitor.X;
            double localY = ev.Y - (_monitor.Y + _monitor.Height - (Constants.StripHeight + Constants.Headroom));
            buf[n++] = new InputEvent(ev.Type, localX, localY, ev.Time);
        }
        if (n == buf.Length) return buf;
        var trimmed = new InputEvent[n];
        Array.Copy(buf, trimmed, n);
        return trimmed;
    }
}
