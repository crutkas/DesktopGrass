using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Xunit;

namespace DesktopGrass.Win2D.Tests;

[SupportedOSPlatform("windows")]
public sealed class ClickThroughSmokeTests
{
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int SW_SHOWNOACTIVATE = 4;
    private const uint LWA_ALPHA = 0x00000002;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint PM_REMOVE = 0x0001;
    private const int INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const uint DESKTOP_SWITCHDESKTOP = 0x0100;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static bool s_probeReceivedLeftDown;

    [Fact]
    [Trait("Category", "smoke")]
    public void OverlayClickThroughAllowsInputToReachWindowsBeneath()
    {
        ClickThroughResult result = SpawnProbeWindowAndClickThroughOverlay();
        if (result == ClickThroughResult.Skipped)
        {
            return;
        }

        Assert.Equal(ClickThroughResult.Passed, result);
    }

    private static ClickThroughResult SpawnProbeWindowAndClickThroughOverlay()
    {
        if (!HasInteractiveDesktop())
        {
            return ClickThroughResult.Skipped;
        }

        s_probeReceivedLeftDown = false;

        string probeClass = $"DesktopGrass.Win2D.ClickThrough.Probe.{Environment.ProcessId}.{Guid.NewGuid():N}";
        string overlayClass = $"DesktopGrass.Win2D.ClickThrough.Overlay.{Environment.ProcessId}.{Guid.NewGuid():N}";
        IntPtr hInstance = GetModuleHandleW(null);

        WndProc probeWndProc = ProbeWndProc;
        WndProc overlayWndProc = OverlayWndProc;
        IntPtr probeWndProcPtr = Marshal.GetFunctionPointerForDelegate(probeWndProc);
        IntPtr overlayWndProcPtr = Marshal.GetFunctionPointerForDelegate(overlayWndProc);

        var probeWc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = probeWndProcPtr,
            hInstance = hInstance,
            lpszClassName = probeClass,
        };
        var overlayWc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = overlayWndProcPtr,
            hInstance = hInstance,
            lpszClassName = overlayClass,
        };

        if (RegisterClassExW(ref probeWc) == 0)
        {
            return ClickThroughResult.Failed;
        }
        if (RegisterClassExW(ref overlayWc) == 0)
        {
            UnregisterClassW(probeClass, hInstance);
            return ClickThroughResult.Failed;
        }

        int x = GetSystemMetrics(SM_XVIRTUALSCREEN) + 96;
        int y = GetSystemMetrics(SM_YVIRTUALSCREEN) + 96;
        const int width = 96;
        const int height = 64;
        int clickX = x + 24;
        int clickY = y + 24;

        IntPtr probe = IntPtr.Zero;
        IntPtr overlay = IntPtr.Zero;
        ClickThroughResult result = ClickThroughResult.Failed;
        try
        {
            probe = CreateWindowExW(
                WS_EX_TOOLWINDOW | WS_EX_TOPMOST,
                probeClass,
                "DesktopGrass click-through probe",
                WS_POPUP | WS_VISIBLE,
                x, y, width, height,
                IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
            if (probe == IntPtr.Zero)
            {
                return ClickThroughResult.Failed;
            }
            SetWindowPos(probe, HWND_TOPMOST, x, y, width, height, SWP_SHOWWINDOW);

            overlay = CreateWindowExW(
                WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                overlayClass,
                "DesktopGrass click-through overlay",
                WS_POPUP,
                x, y, width, height,
                IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
            if (overlay == IntPtr.Zero)
            {
                return ClickThroughResult.Failed;
            }

            SetLayeredWindowAttributes(overlay, 0, 1, LWA_ALPHA);
            ShowWindow(overlay, SW_SHOWNOACTIVATE);
            SetWindowPos(overlay, HWND_TOPMOST, x, y, width, height, SWP_SHOWWINDOW | SWP_NOACTIVATE);
            PumpMessagesFor(TimeSpan.FromMilliseconds(50));

            if (!SetCursorPos(clickX, clickY))
            {
                return ClickThroughResult.Skipped;
            }

            INPUT[] inputs =
            [
                new() { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } },
                new() { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } },
            ];
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            if (sent != (uint)inputs.Length)
            {
                return ClickThroughResult.Skipped;
            }

            PumpMessagesFor(TimeSpan.FromMilliseconds(200));
            result = s_probeReceivedLeftDown ? ClickThroughResult.Passed : ClickThroughResult.Failed;
        }
        finally
        {
            if (overlay != IntPtr.Zero) DestroyWindow(overlay);
            if (probe != IntPtr.Zero) DestroyWindow(probe);
            UnregisterClassW(overlayClass, hInstance);
            UnregisterClassW(probeClass, hInstance);
            GC.KeepAlive(probeWndProc);
            GC.KeepAlive(overlayWndProc);
        }

        return result;
    }

    private static bool HasInteractiveDesktop()
    {
        if (!Environment.UserInteractive || GetConsoleWindow() == IntPtr.Zero)
        {
            return false;
        }

        IntPtr desktop = OpenInputDesktop(0, false, DESKTOP_SWITCHDESKTOP);
        if (desktop == IntPtr.Zero)
        {
            return false;
        }

        CloseDesktop(desktop);
        return true;
    }

    private static void PumpMessagesFor(TimeSpan duration)
    {
        Stopwatch sw = Stopwatch.StartNew();
        do
        {
            while (PeekMessageW(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE))
            {
                TranslateMessage(in msg);
                DispatchMessageW(in msg);
            }
            if (s_probeReceivedLeftDown)
            {
                return;
            }
            Thread.Sleep(5);
        }
        while (sw.Elapsed < duration);
    }

    private static IntPtr ProbeWndProc(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam)
    {
        if (msg == WM_LBUTTONDOWN)
        {
            s_probeReceivedLeftDown = true;
        }
        return DefWindowProcW(hwnd, msg, wparam, lparam);
    }

    private static IntPtr OverlayWndProc(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam)
    {
        return DefWindowProcW(hwnd, msg, wparam, lparam);
    }

    private enum ClickThroughResult
    {
        Passed,
        Skipped,
        Failed,
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr Hwnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public POINT Point;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PeekMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(in MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(in MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
