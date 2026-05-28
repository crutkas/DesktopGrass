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
    }
}
'@ -ReferencedAssemblies 'System.Runtime','System.Collections','System.Text.Encoding.Extensions' | Out-Null
}

Add-Type -AssemblyName System.Drawing -ErrorAction SilentlyContinue | Out-Null

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
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)] [string] $ClassName,
        [Parameter(Mandatory)] [int]    $TimeoutSeconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $pid = [uint32]$Process.Id

    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            throw "process exited (code=$($Process.ExitCode)) before window class '$ClassName' appeared"
        }

        # Fast path: global FindWindowExW against the class atom string.
        $hwnd = [DesktopGrass.Smoke.Win32]::FindWindowExW(
            [IntPtr]::Zero, [IntPtr]::Zero, $ClassName, $null)

        if ($hwnd -ne [IntPtr]::Zero) {
            # Confirm ownership: don't accept a same-class window from another
            # process (paranoid, but cheap).
            $owningPid = [uint32]0
            [void][DesktopGrass.Smoke.Win32]::GetWindowThreadProcessId($hwnd, [ref]$owningPid)
            if ($owningPid -eq $pid) {
                return $hwnd
            }
        }

        # Fallback / cross-check: enumerate all top-level windows owned by the
        # process and look for the class. Catches multi-monitor cases where the
        # first window FindWindowExW returns isn't ours.
        $owned = [DesktopGrass.Smoke.Win32]::EnumerateWindowsForProcess($pid, $ClassName)
        if ($owned.Count -gt 0) {
            return [IntPtr]$owned[0]
        }

        Start-Sleep -Milliseconds 100
    }

    throw "timed out after ${TimeoutSeconds}s waiting for window class '$ClassName' from pid $pid"
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
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $ExePath,
        [Parameter(Mandatory)] [string] $WindowClass,
        [int] $StripHeight    = 80,
        [int] $MinUniqueColors = 50,
        [int] $TimeoutSeconds  = 5
    )

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
        $proc = Start-AppForSmoke -ExePath $ExePath
        $hwnd = Wait-ForWindow -Process $proc -ClassName $WindowClass -TimeoutSeconds $TimeoutSeconds
        [void](Assert-ClickThroughExStyles -Hwnd $hwnd)

        # Give Direct2D / Composition a beat to produce its first real frame.
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
                # cleanup failure shouldn't override the original fail reason
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
