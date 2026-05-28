// WindowAttacher.cs
//
// The friction point: WinUI 3's Microsoft.UI.Xaml.Window creates a real
// Win32 HWND under the hood (class "WinUIDesktopWin32WindowClass") which we
// can't rename and don't fully control. To get true click-through topmost
// behaviour we have to *retroactively* punch the right window styles onto
// that HWND with SetWindowLongPtrW after the AppWindow is created.
//
// This is documented as a working approach in the WindowsAppSDK issue
// tracker but is decidedly not first-class — see the friction notes in
// the summary.

using System;
using DesktopGrass.WinUI3.Interop;

namespace DesktopGrass.WinUI3;

internal static class WindowAttacher
{
    /// <summary>
    /// Force the click-through + topmost ExStyle bits onto the supplied HWND
    /// and strip WinUI 3's chrome/border by replacing the window style with
    /// plain WS_POPUP. Idempotent.
    /// </summary>
    public static void MakeClickThroughTopmost(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        // 1. Replace the window style with WS_POPUP | WS_VISIBLE.
        //    WinUI 3 defaults to WS_OVERLAPPEDWINDOW which paints a border
        //    and a caption bar — neither of which we want for a grass strip.
        User32.SetWindowLongPtrW(
            hwnd,
            WindowStyles.GWL_STYLE,
            unchecked((IntPtr)(int)(WindowStyles.WS_POPUP | WindowStyles.WS_VISIBLE)));

        // 2. OR in the click-through ExStyle bits. Don't blow away whatever
        //    WinUI already set; just add ours.
        long currentEx = User32.GetWindowLongPtrW(hwnd, WindowStyles.GWL_EXSTYLE).ToInt64();
        long newEx = currentEx | WindowStyles.ClickThroughExStyles;
        User32.SetWindowLongPtrW(hwnd, WindowStyles.GWL_EXSTYLE, (IntPtr)newEx);

        // 3. WS_EX_LAYERED requires either UpdateLayeredWindow (CPU-side) or
        //    SetLayeredWindowAttributes with a colorkey/alpha. We want
        //    per-pixel alpha from the XAML compositor so we set the layer
        //    to fully opaque (alpha=255) and rely on the compositor for
        //    transparent pixels. This is the workaround for the WinUI 3 +
        //    WS_EX_LAYERED interaction noted in the prompt.
        User32.SetLayeredWindowAttributes(hwnd, 0u, 255, User32.LWA_ALPHA);

        // 4. Force topmost + a SetWindowPos pump so the style changes take
        //    effect immediately (some bits require SWP_FRAMECHANGED to
        //    propagate to the compositor).
        User32.SetWindowPos(
            hwnd, SwpFlags.HWND_TOPMOST, 0, 0, 0, 0,
            SwpFlags.SWP_NOMOVE | SwpFlags.SWP_NOSIZE
                | SwpFlags.SWP_NOACTIVATE | SwpFlags.SWP_NOOWNERZORDER
                | SwpFlags.SWP_FRAMECHANGED);
    }

    /// <summary>
    /// Move + size the window to fit the supplied bottom-strip rectangle
    /// (full screen width, grass-strip + headroom tall, bottom-anchored).
    /// </summary>
    public static void PositionAtMonitorBottom(IntPtr hwnd, int monitorX, int monitorY,
                                               int monitorWidth, int monitorHeight,
                                               int stripHeight, int headroom)
    {
        if (hwnd == IntPtr.Zero) return;

        int windowHeight = stripHeight + headroom;
        int x = monitorX;
        int y = monitorY + monitorHeight - windowHeight;
        int w = monitorWidth;
        int h = windowHeight;

        User32.SetWindowPos(
            hwnd, SwpFlags.HWND_TOPMOST, x, y, w, h,
            SwpFlags.SWP_NOACTIVATE | SwpFlags.SWP_NOOWNERZORDER | SwpFlags.SWP_SHOWWINDOW);
    }
}
