# Smoke.Common.psm1
#
# Shared helpers for DesktopGrass smoke tests.
#
# Design notes:
#   * Custom-rendered windows (Direct2D, DirectComposition, raw XAML Composition)
#     are NOT in the UIA tree in any usable way. Asserting on UIA properties
#     would either find nothing or, worse, produce false positives the same way
#     UIA fallbacks do for WebView2 DOM.
#   * Pixel variance against a fresh screenshot is the only honest "did it
#     actually paint?" signal. That is the source of truth here.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# P/Invoke surface. One Add-Type call so re-importing the module is cheap and
# we never hit the "type already defined in this AppDomain" failure mode.
# ---------------------------------------------------------------------------

if (-not ('DesktopGrass.Smoke.Win32' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopGrass.Smoke
{
    public static class Win32
    {
        public const int GWL_EXSTYLE = -20;
        public const int SM_CMONITORS = 80;
        public const uint WM_CLOSE = 0x0010;
        public const uint MONITOR_DEFAULTTONULL = 0;
        public const int SW_SHOW = 5;
        public const int SW_SHOWNOACTIVATE = 4;
        public const byte VK_MENU = 0x12;
        public const uint KEYEVENTF_KEYUP = 0x0002;

        public const long WS_EX_LAYERED     = 0x00080000;
        public const long WS_EX_TRANSPARENT = 0x00000020;
        public const long WS_EX_TOPMOST     = 0x00000008;
        public const long WS_EX_TOOLWINDOW  = 0x00000080;
        public const long WS_EX_NOACTIVATE  = 0x08000000;
        public const uint WS_POPUP          = 0x80000000;
        public const uint WS_VISIBLE        = 0x10000000;
        public const uint SS_BLACKRECT      = 0x00000004;
        public const uint SWP_NOACTIVATE    = 0x0010;
        public const uint SWP_SHOWWINDOW    = 0x0040;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        // Message-only windows (created with a HWND_MESSAGE parent, such as
        // App's kMsgWindowClass) are not top-level and never appear via
        // EnumWindows/FindWindowExW(NULL, ...). They must be looked up as
        // children of this pseudo-handle.
        public static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        // Rights required by GetProcessInformation(ProcessPowerThrottling);
        // deliberately narrower than PROCESS_QUERY_INFORMATION.
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        // PROCESS_INFORMATION_CLASS::ProcessPowerThrottling (winnt.h).
        public const int ProcessPowerThrottling = 4;
        public const uint WTS_CURRENT_SESSION = 0xFFFFFFFF;
        public const int WTSSessionInfoEx = 25;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        // PROCESS_POWER_THROTTLING_STATE (winnt.h). ControlMask/StateMask bit
        // 0x1 is PROCESS_POWER_THROTTLING_EXECUTION_SPEED (EcoQoS).
        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessPowerThrottlingState
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        public sealed class SessionStateSnapshot
        {
            public bool Success { get; set; }
            public int ErrorCode { get; set; }
            public uint BytesReturned { get; set; }
            public uint Level { get; set; }
            public uint SessionId { get; set; }
            public int ConnectState { get; set; }
            public int SessionFlags { get; set; }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(
            uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetProcessInformation(
            IntPtr hProcess,
            int processInformationClass,
            ref ProcessPowerThrottlingState processInformation,
            uint processInformationSize);

        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool WTSQuerySessionInformationW(
            IntPtr server,
            uint sessionId,
            int infoClass,
            out IntPtr buffer,
            out uint bytesReturned);

        [DllImport("wtsapi32.dll")]
        public static extern void WTSFreeMemory(IntPtr memory);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindowExW(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetClassNameW(IntPtr hwnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetWindowTextW(IntPtr hwnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetWindowTextLengthW(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        // 64-bit safe variant; on 32-bit hosts CLR will marshal to GetWindowLongW.
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        public static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SendMessageTimeoutW(
            IntPtr hwnd,
            uint msg,
            IntPtr wParam,
            IntPtr lParam,
            uint flags,
            uint timeoutMs,
            out IntPtr result);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowExW(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int width,
            int height,
            IntPtr hwndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetActiveWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool BringWindowToTop(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll")]
        public static extern void SwitchToThisWindow(IntPtr hwnd, bool altTab);

        [DllImport("user32.dll")]
        public static extern void keybd_event(
            byte virtualKey,
            byte scanCode,
            uint flags,
            UIntPtr extraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool AttachThreadInput(
            uint idAttach,
            uint idAttachTo,
            bool attach);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(
            IntPtr hwnd,
            IntPtr hwndInsertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateWindow(IntPtr hwnd);

        public static IntPtr CreateOpaqueProbeWindow(
            string title,
            int x,
            int y,
            int width,
            int height,
            bool topmost,
            bool activate)
        {
            uint exStyle = topmost ? (uint)WS_EX_TOPMOST : 0;
            if (!activate) exStyle |= (uint)WS_EX_NOACTIVATE;

            IntPtr hwnd = CreateWindowExW(
                exStyle,
                "STATIC",
                title,
                WS_POPUP | WS_VISIBLE | SS_BLACKRECT,
                x,
                y,
                width,
                height,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);
            if (hwnd == IntPtr.Zero) return IntPtr.Zero;

            uint flags = SWP_SHOWWINDOW;
            if (!activate) flags |= SWP_NOACTIVATE;
            if (!SetWindowPos(
                    hwnd,
                    topmost ? HWND_TOPMOST : IntPtr.Zero,
                    x, y, width, height, flags))
            {
                DestroyWindow(hwnd);
                return IntPtr.Zero;
            }
            UpdateWindow(hwnd);
            if (activate) ActivateWindow(hwnd);
            return hwnd;
        }

        public static bool ActivateWindow(IntPtr hwnd)
        {
            uint currentThread = GetCurrentThreadId();
            uint foregroundProcess;
            uint foregroundThread = GetWindowThreadProcessId(
                GetForegroundWindow(), out foregroundProcess);
            bool attached = foregroundThread != 0
                && foregroundThread != currentThread
                && AttachThreadInput(currentThread, foregroundThread, true);
            try
            {
                ShowWindow(hwnd, SW_SHOW);
                BringWindowToTop(hwnd);
                SetActiveWindow(hwnd);
                keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
                SetForegroundWindow(hwnd);
                keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                if (GetForegroundWindow() != hwnd)
                {
                    SwitchToThisWindow(hwnd, true);
                }
                return GetForegroundWindow() == hwnd;
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(currentThread, foregroundThread, false);
                }
            }
        }

        public static List<IntPtr> EnumerateWindowsForProcess(uint processId, string className)
        {
            var matches = new List<IntPtr>();
            EnumWindows((hwnd, lParam) =>
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid != processId) return true;

                var sb = new StringBuilder(256);
                GetClassNameW(hwnd, sb, sb.Capacity);
                if (string.Equals(sb.ToString(), className, StringComparison.Ordinal))
                {
                    matches.Add(hwnd);
                }
                return true;
            }, IntPtr.Zero);
            return matches;
        }

        // Enumerates every top-level window owned by the given pid, regardless
        // of class. The TitleMatch path uses this to do a regex test against
        // each title in PowerShell.
        public static List<IntPtr> EnumerateAllWindowsForProcess(uint processId)
        {
            var matches = new List<IntPtr>();
            EnumWindows((hwnd, lParam) =>
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == processId)
                {
                    matches.Add(hwnd);
                }
                return true;
            }, IntPtr.Zero);
            return matches;
        }

        public static string GetWindowTitle(IntPtr hwnd)
        {
            int len = GetWindowTextLengthW(hwnd);
            if (len <= 0) return string.Empty;
            var sb = new StringBuilder(len + 1);
            int read = GetWindowTextW(hwnd, sb, sb.Capacity);
            if (read <= 0) return string.Empty;
            return sb.ToString();
        }

        public static int AssertMonitorTopology(
            uint processId,
            string className,
            double surfaceHeightDip)
        {
            // Make all geometry APIs return physical pixels, matching the PMv2
            // Native process rather than virtualizing into the PowerShell host.
            IntPtr previousContext =
                SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                List<IntPtr> windows =
                    EnumerateWindowsForProcess(processId, className);
                int monitorCount = GetSystemMetrics(SM_CMONITORS);
                if (monitorCount <= 0)
                {
                    throw new InvalidOperationException(
                        "Windows reported no active display monitors.");
                }
                if (windows.Count != monitorCount)
                {
                    throw new InvalidOperationException(
                        "monitor surface count mismatch: found "
                        + windows.Count + " windows for " + monitorCount
                        + " active monitors.");
                }

                var claimedMonitors = new HashSet<IntPtr>();
                foreach (IntPtr hwnd in windows)
                {
                    long exStyle = GetWindowLongPtrW(hwnd, GWL_EXSTYLE).ToInt64();
                    long requiredStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT
                        | WS_EX_TOPMOST | WS_EX_TOOLWINDOW
                        | WS_EX_NOACTIVATE;
                    if ((exStyle & requiredStyle) != requiredStyle)
                    {
                        throw new InvalidOperationException(
                            "monitor grass window is not click-through/topmost: "
                            + hwnd + " exStyle=0x" + exStyle.ToString("X") + ".");
                    }

                    IntPtr monitor =
                        MonitorFromWindow(hwnd, MONITOR_DEFAULTTONULL);
                    if (monitor == IntPtr.Zero)
                    {
                        throw new InvalidOperationException(
                            "grass window is not on an active monitor: " + hwnd);
                    }
                    if (!claimedMonitors.Add(monitor))
                    {
                        throw new InvalidOperationException(
                            "multiple grass windows map to the same monitor.");
                    }

                    RECT windowRect;
                    if (!GetWindowRect(hwnd, out windowRect))
                    {
                        throw new InvalidOperationException(
                            "GetWindowRect failed for grass window: " + hwnd);
                    }

                    var monitorInfo = new MONITORINFO();
                    monitorInfo.cbSize =
                        (uint)Marshal.SizeOf(typeof(MONITORINFO));
                    if (!GetMonitorInfoW(monitor, ref monitorInfo))
                    {
                        throw new InvalidOperationException(
                            "GetMonitorInfoW failed for grass window: " + hwnd);
                    }

                    uint dpi = GetDpiForWindow(hwnd);
                    if (dpi == 0)
                    {
                        throw new InvalidOperationException(
                            "GetDpiForWindow returned zero for: " + hwnd);
                    }
                    int expectedHeight = (int)Math.Floor(
                        surfaceHeightDip * dpi / 96.0 + 0.5);

                    if (windowRect.Left != monitorInfo.rcWork.Left
                        || windowRect.Right != monitorInfo.rcWork.Right
                        || windowRect.Bottom != monitorInfo.rcWork.Bottom
                        || windowRect.Bottom - windowRect.Top != expectedHeight)
                    {
                        throw new InvalidOperationException(
                            "grass window bounds do not match monitor work area "
                            + "and DPI: hwnd=" + hwnd
                            + " window=(" + windowRect.Left + ","
                            + windowRect.Top + "," + windowRect.Right + ","
                            + windowRect.Bottom + ") work=("
                            + monitorInfo.rcWork.Left + ","
                            + monitorInfo.rcWork.Top + ","
                            + monitorInfo.rcWork.Right + ","
                            + monitorInfo.rcWork.Bottom + ") dpi=" + dpi
                            + " expectedHeight=" + expectedHeight + ".");
                    }
                }
                return windows.Count;
            }
            finally
            {
                if (previousContext != IntPtr.Zero)
                {
                    SetThreadDpiAwarenessContext(previousContext);
                }
            }
        }

        public static SessionStateSnapshot QueryCurrentSessionState()
        {
            var result = new SessionStateSnapshot();
            IntPtr buffer = IntPtr.Zero;
            uint bytesReturned = 0;
            if (!WTSQuerySessionInformationW(
                    IntPtr.Zero,
                    WTS_CURRENT_SESSION,
                    WTSSessionInfoEx,
                    out buffer,
                    out bytesReturned))
            {
                result.ErrorCode = Marshal.GetLastWin32Error();
                return result;
            }

            try
            {
                result.BytesReturned = bytesReturned;
                if (bytesReturned < 20)
                {
                    result.ErrorCode = 122; // ERROR_INSUFFICIENT_BUFFER
                    return result;
                }

                result.Level = unchecked((uint)Marshal.ReadInt32(buffer, 0));
                if (result.Level != 1)
                {
                    result.ErrorCode = 13; // ERROR_INVALID_DATA
                    return result;
                }

                // WTSINFOEXW.Data is 8-byte aligned after the 4-byte Level.
                result.SessionId =
                    unchecked((uint)Marshal.ReadInt32(buffer, 8));
                result.ConnectState = Marshal.ReadInt32(buffer, 12);
                result.SessionFlags = Marshal.ReadInt32(buffer, 16);
                result.Success = true;
                return result;
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    WTSFreeMemory(buffer);
                }
            }
        }
    }
}
'@ -ReferencedAssemblies 'System.Runtime','System.Collections','System.Text.Encoding.Extensions' | Out-Null
}

Add-Type -AssemblyName System.Drawing -ErrorAction SilentlyContinue | Out-Null
Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue | Out-Null

# ---------------------------------------------------------------------------
# Public helpers
# ---------------------------------------------------------------------------

function Start-AppForSmoke {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $ExePath
    )

    if (-not (Test-Path -LiteralPath $ExePath)) {
        throw "exe not found: $ExePath"
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = (Resolve-Path -LiteralPath $ExePath).Path
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $false
    $startInfo.WorkingDirectory = Split-Path -Parent $startInfo.FileName

    $proc = [System.Diagnostics.Process]::new()
    $proc.StartInfo = $startInfo
    if (-not $proc.Start()) {
        throw "failed to start process: $ExePath"
    }
    return $proc
}

function Wait-ForWindow {
    <#
    .SYNOPSIS
        Waits for a top-level window owned by the given process to appear,
        matching either by Win32 class name (exact) or window title (regex).

    .DESCRIPTION
        At least one of -ClassName / -TitleMatch must be supplied. If both are
        provided, the window must satisfy BOTH (class equality AND title regex).

        TitleMatch is the canonical path for WinUI 3 targets: the WinUI 3
        framework owns the window class name ('WinUIDesktopWin32WindowClass')
        and re-uses it for any Microsoft.UI.Xaml.Window, so matching by class
        cannot disambiguate our window from anything else WinUI hosts in the
        same process. Each target sets AppWindow.Title to a known string
        instead and the harness regex-matches it.
    #>
    [CmdletBinding(DefaultParameterSetName='ByClass')]
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process] $Process,

        [Parameter(ParameterSetName='ByClass',     Mandatory)]
        [Parameter(ParameterSetName='ClassAndTitle', Mandatory)]
        [string] $ClassName,

        [Parameter(ParameterSetName='ByTitle',     Mandatory)]
        [Parameter(ParameterSetName='ClassAndTitle', Mandatory)]
        [string] $TitleMatch,

        [Parameter(Mandatory)] [int] $TimeoutSeconds
    )

    if (-not $PSBoundParameters.ContainsKey('ClassName') -and -not $PSBoundParameters.ContainsKey('TitleMatch')) {
        throw "Wait-ForWindow requires at least one of -ClassName / -TitleMatch."
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $pid = [uint32]$Process.Id

    $titleRegex = $null
    if ($PSBoundParameters.ContainsKey('TitleMatch')) {
        $titleRegex = [System.Text.RegularExpressions.Regex]::new(
            $TitleMatch,
            [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    }

    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            $what = if ($titleRegex) { "title /${TitleMatch}/" } else { "class '$ClassName'" }
            throw "process exited (code=$($Process.ExitCode)) before window $what appeared"
        }

        # Class-only fast path keeps the original FindWindowExW behaviour.
        if ($PSCmdlet.ParameterSetName -eq 'ByClass') {
            $hwnd = [DesktopGrass.Smoke.Win32]::FindWindowExW(
                [IntPtr]::Zero, [IntPtr]::Zero, $ClassName, $null)

            if ($hwnd -ne [IntPtr]::Zero) {
                $owningPid = [uint32]0
                [void][DesktopGrass.Smoke.Win32]::GetWindowThreadProcessId($hwnd, [ref]$owningPid)
                if ($owningPid -eq $pid) {
                    return $hwnd
                }
            }

            $owned = [DesktopGrass.Smoke.Win32]::EnumerateWindowsForProcess($pid, $ClassName)
            if ($owned.Count -gt 0) {
                return [IntPtr]$owned[0]
            }
        }
        else {
            # ByTitle / ClassAndTitle: walk every top-level window owned by
            # the process and test the (optional) class + title regex.
            $all = [DesktopGrass.Smoke.Win32]::EnumerateAllWindowsForProcess($pid)
            foreach ($candidate in $all) {
                $hwnd = [IntPtr]$candidate
                if (-not [DesktopGrass.Smoke.Win32]::IsWindowVisible($hwnd)) {
                    continue
                }
                if ($PSCmdlet.ParameterSetName -eq 'ClassAndTitle') {
                    $sb = [System.Text.StringBuilder]::new(256)
                    [void][DesktopGrass.Smoke.Win32]::GetClassNameW($hwnd, $sb, $sb.Capacity)
                    if ($sb.ToString() -ne $ClassName) { continue }
                }
                $title = [DesktopGrass.Smoke.Win32]::GetWindowTitle($hwnd)
                if ([string]::IsNullOrEmpty($title)) { continue }
                if ($titleRegex.IsMatch($title)) {
                    return $hwnd
                }
            }
        }

        Start-Sleep -Milliseconds 100
    }

    $what = if ($titleRegex) { "title /${TitleMatch}/" } else { "class '$ClassName'" }
    throw "timed out after ${TimeoutSeconds}s waiting for window $what from pid $pid"
}

function Assert-ClickThroughExStyles {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [IntPtr] $Hwnd
    )

    $raw = [DesktopGrass.Smoke.Win32]::GetWindowLongPtrW($Hwnd, [DesktopGrass.Smoke.Win32]::GWL_EXSTYLE)
    $exStyle = [int64]$raw.ToInt64()

    $required = [ordered]@{
        'WS_EX_LAYERED'     = [DesktopGrass.Smoke.Win32]::WS_EX_LAYERED
        'WS_EX_TRANSPARENT' = [DesktopGrass.Smoke.Win32]::WS_EX_TRANSPARENT
        'WS_EX_TOPMOST'     = [DesktopGrass.Smoke.Win32]::WS_EX_TOPMOST
        'WS_EX_TOOLWINDOW'  = [DesktopGrass.Smoke.Win32]::WS_EX_TOOLWINDOW
        'WS_EX_NOACTIVATE'  = [DesktopGrass.Smoke.Win32]::WS_EX_NOACTIVATE
    }

    $missing = @()
    foreach ($name in $required.Keys) {
        $bit = [int64]$required[$name]
        if (($exStyle -band $bit) -ne $bit) {
            $missing += $name
        }
    }

    if ($missing.Count -gt 0) {
        $hex = '0x{0:X8}' -f $exStyle
        throw "click-through ExStyle assertion failed on hwnd=$Hwnd; missing bits: $($missing -join ', ') (actual ExStyle=$hex)"
    }

    return $true
}

function Wait-ForWindowVisibility {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [IntPtr] $Hwnd,
        [Parameter(Mandatory)] [bool] $Visible,
        [int] $TimeoutSeconds = 5
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ([DesktopGrass.Smoke.Win32]::IsWindowVisible($Hwnd) -eq $Visible) {
            return
        }
        Start-Sleep -Milliseconds 50
    }

    $state = if ($Visible) { 'visible' } else { 'hidden' }
    throw "timed out after ${TimeoutSeconds}s waiting for hwnd=$Hwnd to become $state"
}

function New-OpaqueProbeWindow {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Title,
        [Parameter(Mandatory)] [System.Drawing.Rectangle] $Bounds,
        [switch] $Topmost,
        [switch] $Activate
    )

    $hwnd = [DesktopGrass.Smoke.Win32]::CreateOpaqueProbeWindow(
        $Title,
        $Bounds.X,
        $Bounds.Y,
        $Bounds.Width,
        $Bounds.Height,
        $Topmost.IsPresent,
        $Activate.IsPresent)
    if ($hwnd -eq [IntPtr]::Zero) {
        $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        throw "failed to create opaque probe window (Win32 error $errorCode)"
    }
    return $hwnd
}

function Get-InteractiveSessionState {
    [CmdletBinding()]
    param()

    $snapshot = [DesktopGrass.Smoke.Win32]::QueryCurrentSessionState()
    if (-not $snapshot.Success) {
        return [pscustomobject]@{
            status = 'error'
            reason = "WTS session query failed (Win32 error $($snapshot.ErrorCode))"
            session_id = $null
            connect_state = 'unknown'
            lock_state = 'unknown'
            interactive_active = $false
        }
    }

    $connectStates = @(
        'active',
        'connected',
        'connect_query',
        'shadow',
        'disconnected',
        'idle',
        'listen',
        'reset',
        'down',
        'initializing'
    )
    $connectState = if (
        $snapshot.ConnectState -ge 0 -and
        $snapshot.ConnectState -lt $connectStates.Count
    ) {
        $connectStates[$snapshot.ConnectState]
    } else {
        'unknown'
    }
    $lockState = switch ($snapshot.SessionFlags) {
        0 { 'locked' }
        1 { 'unlocked' }
        default { 'unknown' }
    }

    return [pscustomobject]@{
        status = 'available'
        reason = $null
        session_id = $snapshot.SessionId
        connect_state = $connectState
        lock_state = $lockState
        interactive_active = (
            $connectState -eq 'active' -and
            $lockState -eq 'unlocked'
        )
    }
}

function Remove-ProbeWindow {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [IntPtr] $Hwnd
    )

    if ($Hwnd -ne [IntPtr]::Zero) {
        [void][DesktopGrass.Smoke.Win32]::DestroyWindow($Hwnd)
    }
}

function Get-GrassStripPixelVariance {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [int] $StripHeight,
        [IntPtr] $Hwnd = [IntPtr]::Zero
    )

    $previousContext = [IntPtr]::Zero
    try {
        if ($Hwnd -ne [IntPtr]::Zero) {
            $previousContext =
                [DesktopGrass.Smoke.Win32]::SetThreadDpiAwarenessContext(
                    [IntPtr]::new(-4))
            $windowRect = [DesktopGrass.Smoke.Win32+RECT]::new()
            if (-not [DesktopGrass.Smoke.Win32]::GetWindowRect(
                    $Hwnd, [ref]$windowRect)) {
                throw "GetWindowRect failed for rendering probe hwnd=$Hwnd."
            }
            $width = [int]($windowRect.Right - $windowRect.Left)
            $top = [int]($windowRect.Bottom - $StripHeight)
            $left = [int]$windowRect.Left
        } else {
            $primary = [System.Windows.Forms.Screen]::PrimaryScreen
            if ($null -eq $primary) {
                Add-Type -AssemblyName System.Windows.Forms | Out-Null
                $primary = [System.Windows.Forms.Screen]::PrimaryScreen
            }
            $workArea = $primary.WorkingArea
            $width  = [int]$workArea.Width
            $top    = [int]($workArea.Y + $workArea.Height - $StripHeight)
            $left   = [int]$workArea.X
        }

        $bmp = [System.Drawing.Bitmap]::new(
            $width,
            $StripHeight,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $unique = [System.Collections.Generic.HashSet[int]]::new()
        try {
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try {
                $g.CopyFromScreen(
                    $left,
                    $top,
                    0,
                    0,
                    [System.Drawing.Size]::new($width, $StripHeight))
            } finally {
                $g.Dispose()
            }

            $step = 4
            for ($y = 0; $y -lt $StripHeight; $y += $step) {
                for ($x = 0; $x -lt $width; $x += $step) {
                    $argb = $bmp.GetPixel($x, $y).ToArgb()
                    [void]$unique.Add($argb)
                }
            }
            return $unique.Count
        } finally {
            $bmp.Dispose()
        }
    } finally {
        if ($previousContext -ne [IntPtr]::Zero) {
            [void][DesktopGrass.Smoke.Win32]::SetThreadDpiAwarenessContext(
                $previousContext)
        }
    }
}

function Assert-GrassRendered {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [int] $StripHeight,
        [Parameter(Mandatory)] [int] $MinUniqueColors,
        [IntPtr] $Hwnd = [IntPtr]::Zero,
        [int] $TimeoutSeconds = 5
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $count = 0
    do {
        $count = Get-GrassStripPixelVariance -StripHeight $StripHeight -Hwnd $Hwnd
        if ($count -ge $MinUniqueColors) {
            return $count
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "grass strip pixel variance too low: $count unique colors (expected >= $MinUniqueColors). Nothing meaningful drew."
}

function Assert-MonitorSurfaceTopology {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)] [string] $WindowClass,
        [double] $SurfaceHeightDip = 110,
        [int] $TimeoutSeconds = 5
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastError = $null
    do {
        if ($Process.HasExited) {
            throw "process exited before monitor surfaces stabilized."
        }
        try {
            return [DesktopGrass.Smoke.Win32]::AssertMonitorTopology(
                [uint32]$Process.Id,
                $WindowClass,
                $SurfaceHeightDip)
        } catch {
            $lastError = $_.Exception
            Start-Sleep -Milliseconds 100
        }
    } while ([DateTime]::UtcNow -lt $deadline)

    throw $lastError
}

function Find-MessageOnlyWindow {
    <#
    .SYNOPSIS
        Looks up a message-only window (created with an HWND_MESSAGE parent,
        e.g. App's kMsgWindowClass) owned by the given process.

    .DESCRIPTION
        Message-only windows never appear via EnumWindows or
        FindWindowExW(NULL, ...); they only enumerate as children of the
        HWND_MESSAGE pseudo-handle. Returns [IntPtr]::Zero if no match is
        found or the match is not owned by -Process.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)] [string] $ClassName,
        [Parameter(Mandatory)] [string] $Title
    )

    $after = [IntPtr]::Zero
    while ($true) {
        $hwnd = [DesktopGrass.Smoke.Win32]::FindWindowExW(
            [DesktopGrass.Smoke.Win32]::HWND_MESSAGE,
            $after,
            $ClassName,
            $Title)
        if ($hwnd -eq [IntPtr]::Zero) {
            return [IntPtr]::Zero
        }

        $owningPid = [uint32]0
        [void][DesktopGrass.Smoke.Win32]::GetWindowThreadProcessId(
            $hwnd,
            [ref]$owningPid)
        if ($owningPid -eq [uint32]$Process.Id) {
            return $hwnd
        }
        $after = $hwnd
    }
}

function Wait-ForMessageOnlyWindow {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)] [string] $ClassName,
        [Parameter(Mandatory)] [string] $Title,
        [Parameter(Mandatory)] [int] $TimeoutSeconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            throw "process exited (code=$($Process.ExitCode)) before message-only window class '$ClassName' title '$Title' appeared"
        }
        $hwnd = Find-MessageOnlyWindow -Process $Process -ClassName $ClassName -Title $Title
        if ($hwnd -ne [IntPtr]::Zero) {
            return $hwnd
        }
        Start-Sleep -Milliseconds 100
    }
    throw "timed out after ${TimeoutSeconds}s waiting for message-only window class '$ClassName' title '$Title' from pid $($Process.Id)"
}

function Get-ProcessPowerThrottlingState {
    <#
    .SYNOPSIS
        Reads GetProcessInformation(ProcessPowerThrottling) for a PID so OS
        execution-speed/EcoQoS state can be recorded separately from
        DesktopGrass's own FPS policy.

    .DESCRIPTION
        Returns an explicit status ('available', 'unsupported', or 'error')
        rather than ever guessing a throttled state. 'unsupported' covers
        Windows versions/builds that reject the ProcessPowerThrottling
        information class.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [int] $ProcessId
    )

    $handle = [DesktopGrass.Smoke.Win32]::OpenProcess(
        [DesktopGrass.Smoke.Win32]::PROCESS_QUERY_LIMITED_INFORMATION,
        $false,
        [uint32]$ProcessId)
    if ($handle -eq [IntPtr]::Zero) {
        $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        return [pscustomobject]@{
            status = 'error'
            reason = "OpenProcess failed (Win32 error $errorCode)."
            execution_speed_throttled = $null
            control_mask = $null
            state_mask = $null
        }
    }

    try {
        $state = [DesktopGrass.Smoke.Win32+ProcessPowerThrottlingState]::new()
        $state.Version = 1
        $size = [uint32][Runtime.InteropServices.Marshal]::SizeOf($state)
        $ok = [DesktopGrass.Smoke.Win32]::GetProcessInformation(
            $handle,
            [DesktopGrass.Smoke.Win32]::ProcessPowerThrottling,
            [ref]$state,
            $size)
        if (-not $ok) {
            $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
            # ERROR_INVALID_PARAMETER (87) is how older builds reject a
            # PROCESS_INFORMATION_CLASS they do not implement.
            $status = if ($errorCode -eq 87) { 'unsupported' } else { 'error' }
            return [pscustomobject]@{
                status = $status
                reason = "GetProcessInformation failed (Win32 error $errorCode)."
                execution_speed_throttled = $null
                control_mask = $null
                state_mask = $null
            }
        }

        $executionSpeedBit = 0x1
        return [pscustomobject]@{
            status = 'available'
            reason = $null
            execution_speed_throttled = (
                (([uint32]$state.ControlMask) -band $executionSpeedBit) -ne 0 -and
                (([uint32]$state.StateMask) -band $executionSpeedBit) -ne 0
            )
            control_mask = [uint32]$state.ControlMask
            state_mask = [uint32]$state.StateMask
        }
    } finally {
        [void][DesktopGrass.Smoke.Win32]::CloseHandle($handle)
    }
}

function Stop-AppGracefully {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)] [IntPtr] $Hwnd,
        [Parameter(Mandatory)] [int]    $TimeoutSeconds
    )

    if ($null -eq $Process) { return }

    try {
        if (-not $Process.HasExited -and $Hwnd -ne [IntPtr]::Zero) {
            [void][DesktopGrass.Smoke.Win32]::PostMessageW(
                $Hwnd,
                [DesktopGrass.Smoke.Win32]::WM_CLOSE,
                [IntPtr]::Zero,
                [IntPtr]::Zero)
        }
    } catch {
        # PostMessage can fail if the window is already torn down; treat as
        # already-exiting and fall through to the wait.
    }

    if (-not $Process.WaitForExit(
        [int]([TimeSpan]::FromSeconds($TimeoutSeconds).TotalMilliseconds)
    )) {
        if (-not $Process.WaitForExit(0)) {
            try {
                Stop-Process -Id $Process.Id -Force -ErrorAction Stop
            } catch {
                if (-not $Process.WaitForExit(0)) {
                    throw
                }
            }
        }
        if (-not $Process.WaitForExit(2000)) {
            throw "process $($Process.Id) did not exit after forced shutdown"
        }
    }
}

function Invoke-AppSmoke {
    <#
    .SYNOPSIS
        Launches the target app and runs the click-through + grass-rendered
        assertions, returning a result hashtable.

    .PARAMETER WindowClass
        Win32 class name to match (exact). Mutually exclusive with TitleMatch
        unless both are supplied (in which case the window must satisfy both).

    .PARAMETER TitleMatch
        Regex matched against each window's title via GetWindowTextW. Use this
        for the WinUI 3 target whose class name is owned by the framework.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $ExePath,
        [string] $WindowClass,
        [string] $TitleMatch,
        [int] $StripHeight    = 80,
        [int] $MinUniqueColors = 50,
        [int] $TimeoutSeconds  = 5,
        [switch] $AssertMonitorTopology,
        [scriptblock] $BeforeLaunch
    )

    if (-not $WindowClass -and -not $TitleMatch) {
        throw "Invoke-AppSmoke requires at least one of -WindowClass / -TitleMatch."
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $result = [ordered]@{
        Pass         = $false
        UniqueColors = 0
        FailReason   = $null
        DurationMs   = 0
    }

    $proc = $null
    $hwnd = [IntPtr]::Zero

    try {
        # BeforeLaunch lets a target prepare for the smoke run (e.g. emit a
        # warm-up trace, register a per-target dependency, write a marker
        # file). It runs synchronously before Start-AppForSmoke. Exceptions
        # propagate and abort the smoke for this target.
        if ($null -ne $BeforeLaunch) {
            & $BeforeLaunch | Out-Null
        }

        $proc = Start-AppForSmoke -ExePath $ExePath

        $waitArgs = @{
            Process        = $proc
            TimeoutSeconds = $TimeoutSeconds
        }
        if ($WindowClass) { $waitArgs.ClassName  = $WindowClass }
        if ($TitleMatch)  { $waitArgs.TitleMatch = $TitleMatch }

        $hwnd = Wait-ForWindow @waitArgs
        [void](Assert-ClickThroughExStyles -Hwnd $hwnd)
        if ($AssertMonitorTopology) {
            [void](Assert-MonitorSurfaceTopology -Process $proc -WindowClass $WindowClass -TimeoutSeconds $TimeoutSeconds)
        }

        $result.UniqueColors = Assert-GrassRendered -StripHeight $StripHeight -MinUniqueColors $MinUniqueColors -Hwnd $hwnd -TimeoutSeconds $TimeoutSeconds
        $result.Pass = $true
    } catch {
        $result.FailReason = $_.Exception.Message
    } finally {
        if ($null -ne $proc) {
            try {
                Stop-AppGracefully -Process $proc -Hwnd $hwnd -TimeoutSeconds 2
            } catch {
                if ($null -eq $result.FailReason) {
                    $result.FailReason = "cleanup failed: $($_.Exception.Message)"
                }
            }
            try { $proc.Dispose() } catch { }
        }
        $sw.Stop()
        $result.DurationMs = [int]$sw.ElapsedMilliseconds
    }

    return [hashtable]$result
}

Export-ModuleMember -Function `
    Start-AppForSmoke, `
    Wait-ForWindow, `
    Wait-ForWindowVisibility, `
    New-OpaqueProbeWindow, `
    Remove-ProbeWindow, `
    Assert-ClickThroughExStyles, `
    Assert-MonitorSurfaceTopology, `
    Get-GrassStripPixelVariance, `
    Assert-GrassRendered, `
    Stop-AppGracefully, `
    Invoke-AppSmoke, `
    Find-MessageOnlyWindow, `
    Wait-ForMessageOnlyWindow, `
    Get-ProcessPowerThrottlingState, `
    Get-InteractiveSessionState
