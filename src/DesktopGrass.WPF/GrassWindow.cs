using System;
using DrawingRectangle = System.Drawing.Rectangle;
using System.Windows;
using System.Windows.Interop;
using DesktopGrass.WPF.Interop;

namespace DesktopGrass.WPF;

internal sealed class GrassWindow : IDisposable
{
    private readonly Window _window;
    private readonly int _widthPx;
    private readonly int _heightPx;
    private readonly double _dpiScale;
    private readonly DrawingRectangle _monitorBoundsPx;
    private readonly GrassCanvas _canvas;
    private bool _disposed;

    public event EventHandler? Closed;

    public Sim Sim { get; }
    public IntPtr Hwnd { get; private set; }
    public int WidthPx => _widthPx;
    public int HeightPx => _heightPx;
    public double DpiScale => _dpiScale;
    public DrawingRectangle MonitorBoundsPx => _monitorBoundsPx;

    public GrassWindow(int xPx, int yPx, int widthPx, int heightPx, double dpiScale,
                       DrawingRectangle monitorBoundsPx, ulong seed, double monitorWidthDip)
    {
        _widthPx = widthPx;
        _heightPx = heightPx;
        _dpiScale = dpiScale;
        _monitorBoundsPx = monitorBoundsPx;

        Sim = new Sim
        {
            Blades = Sim.GenerateBlades(seed, monitorWidthDip, Constants.DEFAULT_DENSITY),
            GroundY = heightPx / dpiScale,
            WindowHeight = heightPx / dpiScale,
        };

        _canvas = new GrassCanvas(Sim)
        {
            Width = widthPx / dpiScale,
            Height = heightPx / dpiScale,
        };

        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Topmost = true,
            ShowActivated = false,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = xPx / dpiScale,
            Top = yPx / dpiScale,
            Width = widthPx / dpiScale,
            Height = heightPx / dpiScale,
            Title = "DesktopGrass.WPF.Window",
            Content = _canvas,
        };
        _window.SourceInitialized += OnSourceInitialized;
        _window.Closed += OnClosed;
        _window.Show();

        Hwnd = new WindowInteropHelper(_window).Handle;
        Win32.SetWindowPos(Hwnd, Win32.HWND_TOPMOST,
            xPx, yPx, widthPx, heightPx,
            Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        Hwnd = new WindowInteropHelper(_window).Handle;
        long ex = Win32.GetWindowLongPtrW(Hwnd, Win32.GWL_EXSTYLE).ToInt64();
        ex |= Win32.WS_EX_TRANSPARENT | Win32.WS_EX_TOOLWINDOW
            | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOPMOST | Win32.WS_EX_LAYERED;
        Win32.SetWindowLongPtrW(Hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Render() => _canvas.InvalidateVisual();

    public void Dispose()
    {
        _disposed = true;
        try
        {
            _window.SourceInitialized -= OnSourceInitialized;
            _window.Closed -= OnClosed;
            _window.Close();
        }
        catch
        {
            // Best-effort cleanup; we're shutting down.
        }
    }
}
