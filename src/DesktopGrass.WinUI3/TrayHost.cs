// TrayHost.cs
//
// Tray icon via H.NotifyIcon. Friction note: H.NotifyIcon.WinUI's
// TaskbarIcon (the "canonical" WinUI 3 tray control) is a FrameworkElement
// designed to be hosted inside a XAML window — it has no public Create()
// method in 2.0.x; creation happens implicitly on Loaded. Since our
// MainWindow is a transparent, click-through grass strip, hosting a tray
// icon inside it isn't practical. So we drop down one layer to
// H.NotifyIcon.Core.TrayIcon, which exposes Create/Show/MessageSink
// directly. This is the same path H.NotifyIcon.WinUI uses internally.

using System;
using H.NotifyIcon.Core;
using H.NotifyIcon.Interop;

namespace DesktopGrass.WinUI3;

internal sealed class TrayHost : IDisposable
{
    private readonly Action _onQuit;
    private TrayIcon? _icon;

    public TrayHost(Action onQuit)
    {
        _onQuit = onQuit;
    }

    public void Show()
    {
        _icon = new TrayIcon
        {
            ToolTip = "DesktopGrass (WinUI 3) — left-click to quit",
        };

        // H.NotifyIcon.Core.TrayIcon doesn't ship a default icon — it
        // requires a non-null Icon to render anything in the system tray.
        // We synthesise a 16x16 green square so the user has something to
        // click without us having to commit a binary .ico to the repo.
        _icon.Icon = CreateSyntheticTrayIconHandle();

        _icon.MessageSink.MouseEventReceived += OnMouseEvent;

        _icon.Create();
    }

    private void OnMouseEvent(MouseEvent ev)
    {
        if (ev == MouseEvent.IconLeftMouseUp)
        {
            _onQuit();
        }
    }

    public void Dispose()
    {
        try { _icon?.Dispose(); } catch { /* shutdown race */ }
        _icon = null;
    }

    // Build a tiny 16x16 GDI bitmap, convert it to an HICON, hand it to
    // H.NotifyIcon. The HICON is owned by the OS for the lifetime of the
    // tray entry; we don't destroy it.
    private static IntPtr CreateSyntheticTrayIconHandle()
    {
        using var bmp = new System.Drawing.Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.FromArgb(0, 0, 0, 0));
            using var brush = new System.Drawing.SolidBrush(
                System.Drawing.Color.FromArgb(unchecked((int)0xFF4C9A2E))); // palette idx 2
            g.FillRectangle(brush, 2, 2, 12, 12);
        }
        return bmp.GetHicon();
    }
}

