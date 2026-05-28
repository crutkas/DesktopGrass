// Program.cs - entry point.

using System;

namespace DesktopGrass.Win2D;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            using var app = new App();
            app.Run();
            return 0;
        }
        catch (Exception ex)
        {
            // Best-effort surfacing for crash diagnostics. In normal use the
            // tray ContextMenu / Quit handles shutdown cleanly.
            Console.Error.WriteLine($"DesktopGrass.Win2D fatal: {ex}");
            return 1;
        }
    }
}
