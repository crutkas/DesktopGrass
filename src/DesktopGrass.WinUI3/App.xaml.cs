// App.xaml.cs
//
// WinUI 3 Application entry point. On launch:
//   1. Enumerate monitors (v1: just primary)
//   2. Create one MainWindow per monitor
//   3. Hand it a Sim and start the render/tick loop
//   4. Install the global low-level mouse hook (process-wide)
//   5. Bring up the tray icon for Quit
//
// Note: WinUI 3 doesn't expose a "static Main" by default — it's generated
// by the WinUI source generator from this App class. We override OnLaunched
// to do all of the above.

using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;

namespace DesktopGrass.WinUI3;

public partial class App : Application
{
    private readonly List<MainWindow> _windows = new();
    private MouseHook? _hook;
    private TrayHost?  _tray;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var monitors = MonitorEnumerator.EnumerateForV1();
        foreach (var m in monitors)
        {
            var w = new MainWindow(m);
            _windows.Add(w);
            w.Activate();
        }

        // Mouse hook fans out into every MainWindow's Sim queue.
        _hook = new MouseHook(ev =>
        {
            foreach (var w in _windows)
            {
                w.EnqueueInputEvent(ev);
            }
        });
        _hook.Install();

        _tray = new TrayHost(onQuit: () =>
        {
            _hook?.Uninstall();
            foreach (var w in _windows) w.Close();
            _windows.Clear();
            _tray?.Dispose();
            // Application.Current.Exit() shuts down the dispatcher.
            Exit();
        });
        _tray.Show();
    }
}
