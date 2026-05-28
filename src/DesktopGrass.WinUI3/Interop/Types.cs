// Interop/Types.cs — Win32 numeric constants the WinUI 3 track touches.

namespace DesktopGrass.WinUI3.Interop;

internal static class WindowStyles
{
    public const int GWL_STYLE   = -16;
    public const int GWL_EXSTYLE = -20;

    // Plain WS_POPUP — no chrome, no border, no caption.
    public const long WS_POPUP   = unchecked((long)0x80000000UL);
    public const long WS_VISIBLE = 0x10000000;

    // Extended styles required by the smoke harness (Smoke.Common.psm1):
    //   WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE
    // Plus WS_EX_TOOLWINDOW so we don't show up in alt-tab.
    public const long WS_EX_TOPMOST     = 0x00000008;
    public const long WS_EX_TRANSPARENT = 0x00000020;
    public const long WS_EX_TOOLWINDOW  = 0x00000080;
    public const long WS_EX_LAYERED     = 0x00080000;
    public const long WS_EX_NOACTIVATE  = 0x08000000;

    public const long ClickThroughExStyles =
        WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
}

internal static class HookConstants
{
    public const int WH_MOUSE_LL = 14;

    public const int WM_MOUSEMOVE   = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
}

internal static class SwpFlags
{
    public const uint SWP_NOSIZE         = 0x0001;
    public const uint SWP_NOMOVE         = 0x0002;
    public const uint SWP_NOACTIVATE     = 0x0010;
    public const uint SWP_NOOWNERZORDER  = 0x0200;
    public const uint SWP_FRAMECHANGED   = 0x0020;
    public const uint SWP_SHOWWINDOW     = 0x0040;

    public static readonly nint HWND_TOPMOST = -1;
}
