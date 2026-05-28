// MonitorEnumerator.cs
//
// For v1 we keep this dead simple: enumerate the primary monitor via
// GetSystemMetrics so the smoke test sees exactly one window. Multi-monitor
// support is on the plan's "future iterations" list anyway.
//
// A real production implementation would call EnumDisplayMonitors and
// build one MainWindow per HMONITOR; the WinUI 3 friction note in the
// summary mentions this.

using System.Collections.Generic;
using DesktopGrass.WinUI3.Interop;

namespace DesktopGrass.WinUI3;

internal readonly record struct MonitorBounds(int X, int Y, int Width, int Height);

internal static class MonitorEnumerator
{
    public static IReadOnlyList<MonitorBounds> EnumerateForV1()
    {
        // Primary monitor in physical pixels. WinUI 3's AppWindow uses
        // physical pixels for Move/Resize too, so no DIP scaling needed
        // at this layer.
        int w = User32.GetSystemMetrics(User32.SM_CXSCREEN);
        int h = User32.GetSystemMetrics(User32.SM_CYSCREEN);
        return new[] { new MonitorBounds(0, 0, w, h) };
    }
}
