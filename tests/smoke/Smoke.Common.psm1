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
        public const uint WM_CLOSE = 0x0010;

        public const long WS_EX_LAYERED     = 0x00080000;
        public const long WS_EX_TRANSPARENT = 0x00000020;
        public const long WS_EX_TOPMOST     = 0x00000008;
        public const long WS_EX_NOACTIVATE  = 0x08000000;

        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

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

        // 64-bit safe variant; on 32-bit hosts CLR will marshal to GetWindowLongW.
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        public static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

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

function Get-GrassStripPixelVariance {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [int] $StripHeight
    )

    # Primary monitor bounds.
    $primary = [System.Windows.Forms.Screen]::PrimaryScreen
    if ($null -eq $primary) {
        Add-Type -AssemblyName System.Windows.Forms | Out-Null
        $primary = [System.Windows.Forms.Screen]::PrimaryScreen
    }
    $bounds = $primary.Bounds
    $width  = [int]$bounds.Width
    $top    = [int]($bounds.Y + $bounds.Height - $StripHeight)
    $left   = [int]$bounds.X

    $bmp = [System.Drawing.Bitmap]::new($width, $StripHeight, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $unique = [System.Collections.Generic.HashSet[int]]::new()
    try {
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.CopyFromScreen($left, $top, 0, 0, [System.Drawing.Size]::new($width, $StripHeight))
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
    } finally {
        $bmp.Dispose()
    }

    return $unique.Count
}

function Assert-GrassRendered {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [int] $StripHeight,
        [Parameter(Mandatory)] [int] $MinUniqueColors
    )

    $count = Get-GrassStripPixelVariance -StripHeight $StripHeight
    if ($count -lt $MinUniqueColors) {
        throw "grass strip pixel variance too low: $count unique colors (expected >= $MinUniqueColors). Nothing meaningful drew."
    }
    return $count
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

    if (-not $Process.WaitForExit([int]([TimeSpan]::FromSeconds($TimeoutSeconds).TotalMilliseconds))) {
        try { Stop-Process -Id $Process.Id -Force -ErrorAction Stop } catch { }
        [void]$Process.WaitForExit(2000)
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

        # Give the renderer (Direct2D / Composition / XAML composition) a
        # beat to produce its first real frame.
        Start-Sleep -Milliseconds 1500

        $result.UniqueColors = Assert-GrassRendered -StripHeight $StripHeight -MinUniqueColors $MinUniqueColors
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
    Assert-ClickThroughExStyles, `
    Get-GrassStripPixelVariance, `
    Assert-GrassRendered, `
    Stop-AppGracefully, `
    Invoke-AppSmoke
