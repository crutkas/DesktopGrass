// TrayIcon.cs - WinForms NotifyIcon hosted on a dedicated message-loop
// thread. The main thread runs the grass windows; this thread runs the
// hidden form that owns the tray icon. On Quit we PostMessage WM_APP+1
// to the main thread to break the render loop.

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using DesktopGrass.Win2D.Interop;

namespace DesktopGrass.Win2D;

internal sealed class TrayIcon : IDisposable
{
    public const uint WM_APP_QUIT = Win32.WM_APP + 1;

    private readonly uint _mainThreadId;
    private readonly Scene _initialScene;
    private readonly CritterKind _initialCritter;
    private Thread? _thread;
    private NotifyIcon? _icon;
    private Form? _hiddenForm;
    private readonly ManualResetEventSlim _started = new(false);

    public TrayIcon(uint mainThreadId, Scene initialScene, CritterKind initialCritter)
    {
        _mainThreadId = mainThreadId;
        _initialScene = initialScene;
        _initialCritter = initialCritter;
    }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "DesktopGrass.Win2D.Tray" };
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

        var sceneMenu = new ToolStripMenuItem("Scene");
        var grassItem  = new ToolStripMenuItem("Grass")  { Tag = Scene.Grass,  CheckOnClick = false };
        var desertItem = new ToolStripMenuItem("Desert") { Tag = Scene.Desert, CheckOnClick = false };
        var winterItem = new ToolStripMenuItem("Winter") { Tag = Scene.Winter, CheckOnClick = false };
        var sceneItems = new[] { grassItem, desertItem, winterItem };

        void SelectScene(Scene s)
        {
            foreach (var it in sceneItems)
                it.Checked = ((Scene)it.Tag!) == s;
            Win32App.RequestSceneChange(s);
        }
        grassItem.Click  += (_, _) => SelectScene(Scene.Grass);
        desertItem.Click += (_, _) => SelectScene(Scene.Desert);
        winterItem.Click += (_, _) => SelectScene(Scene.Winter);
        SelectScene(_initialScene);

        sceneMenu.DropDownItems.AddRange(sceneItems);
        menu.Items.Add(sceneMenu);

        // Critter submenu (§13.3 / §16 / §17). Independent of Scene — pick
        // a pet to wander on top of whatever biome is active.
        var critterMenu = new ToolStripMenuItem("Critter");
        var critterNoneItem  = new ToolStripMenuItem("None")  { Tag = CritterKind.None,  CheckOnClick = false };
        var critterSheepItem = new ToolStripMenuItem("Sheep") { Tag = CritterKind.Sheep, CheckOnClick = false };
        var critterCatItem   = new ToolStripMenuItem("Cat")   { Tag = CritterKind.Cat,   CheckOnClick = false };
        var critterItems = new[] { critterNoneItem, critterSheepItem, critterCatItem };

        void SelectCritter(CritterKind c)
        {
            foreach (var it in critterItems)
                it.Checked = ((CritterKind)it.Tag!) == c;
            Win32App.RequestCritterChange(c);
        }
        critterNoneItem.Click  += (_, _) => SelectCritter(CritterKind.None);
        critterSheepItem.Click += (_, _) => SelectCritter(CritterKind.Sheep);
        critterCatItem.Click   += (_, _) => SelectCritter(CritterKind.Cat);
        SelectCritter(_initialCritter);

        critterMenu.DropDownItems.AddRange(critterItems);
        menu.Items.Add(critterMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quitItem);

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "DesktopGrass (Win2D)",
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
