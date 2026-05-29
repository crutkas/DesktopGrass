// TrayIcon.cs - WinForms NotifyIcon hosted on a dedicated message-loop
// thread. The main thread runs the grass windows; this thread runs the
// hidden form that owns the tray icon. On Quit we PostMessage WM_APP+1
// to the main thread to break the render loop.

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using DesktopGrass.WPF.Interop;

namespace DesktopGrass.WPF;

internal sealed class TrayIcon : IDisposable
{
    public const uint WM_APP_QUIT = Win32.WM_APP + 1;

    private readonly uint _mainThreadId;
    private Thread? _thread;
    private NotifyIcon? _icon;
    private Form? _hiddenForm;
    private readonly ManualResetEventSlim _started = new(false);

    public TrayIcon(uint mainThreadId)
    {
        _mainThreadId = mainThreadId;
    }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "DesktopGrass.WPF.Tray" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        // Wait until the form is up so disposal is well-ordered.
        _started.Wait(TimeSpan.FromSeconds(2));
    }

    private void Run()
    {
        _hiddenForm = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            Opacity = 0,
            Width = 1, Height = 1,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-2000, -2000),
        };

        var menu = new ContextMenuStrip();
        var quitItem = new ToolStripMenuItem("Quit") { Name = "QuitItem" };
        quitItem.Click += (_, _) => RequestQuit();
        menu.Items.Add(quitItem);

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "DesktopGrass (WPF)",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => RequestQuit();

        // The form must exist so Application.Run has a message loop owner.
        _hiddenForm.Load += (_, _) => _started.Set();
        Application.Run(_hiddenForm);

        _icon.Visible = false;
        _icon.Dispose();
        _hiddenForm.Dispose();
    }

    private void RequestQuit()
    {
        // PostThreadMessage equivalent: PostMessage to the main thread's
        // hidden message-only window would be more robust, but a thread-
        // targeted message is the simplest path. We use PostMessage with
        // HWND=0 + the thread id via PostThreadMessageW... but P/Invoke
        // surface above doesn't expose it; instead we ask each grass
        // window to close, which exits the main loop.
        Win32App.SignalQuit();
    }

    public void Dispose()
    {
        try
        {
            if (_hiddenForm is { IsDisposed: false })
            {
                _hiddenForm.BeginInvoke((Action)(() => _hiddenForm.Close()));
            }
            _thread?.Join(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Best-effort cleanup; we're shutting down.
        }
    }
}

