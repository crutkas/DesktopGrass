# Run-NativeRuntimeControlTests.ps1
#
# Interactive Native-only smoke coverage for event-driven fullscreen and
# occlusion suppression. Run from the repo root after building the Native app.

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',

    [int] $TimeoutSeconds = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Smoke.Common.psm1') -Force

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$exePath = Join-Path $repoRoot (
    "src\DesktopGrass.Native\out\$Platform\$Configuration\DesktopGrass.Native.exe")
$windowClass = 'DesktopGrass.Native.Window'

function Get-GrassWindows {
    param([Parameter(Mandatory)] [System.Diagnostics.Process] $Process)

    return @(
        [DesktopGrass.Smoke.Win32]::EnumerateWindowsForProcess(
            [uint32]$Process.Id,
            $windowClass))
}

function Get-WindowBounds {
    param([Parameter(Mandatory)] [IntPtr] $Hwnd)

    $rect = [DesktopGrass.Smoke.Win32+RECT]::new()
    if (-not [DesktopGrass.Smoke.Win32]::GetWindowRect($Hwnd, [ref]$rect)) {
        $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        throw "GetWindowRect failed for hwnd=$Hwnd (Win32 error $errorCode)"
    }
    return [System.Drawing.Rectangle]::FromLTRB(
        $rect.Left, $rect.Top, $rect.Right, $rect.Bottom)
}

function Find-PrimaryGrassWindow {
    param(
        [Parameter(Mandatory)] [IntPtr[]] $Windows,
        [Parameter(Mandatory)] [System.Drawing.Rectangle] $PrimaryBounds
    )

    $best = [IntPtr]::Zero
    $bestArea = 0L
    foreach ($hwnd in $Windows) {
        $intersection = [System.Drawing.Rectangle]::Intersect(
            (Get-WindowBounds -Hwnd $hwnd),
            $PrimaryBounds)
        $area = [int64]$intersection.Width * [int64]$intersection.Height
        if ($area -gt $bestArea) {
            $best = $hwnd
            $bestArea = $area
        }
    }
    if ($best -eq [IntPtr]::Zero) {
        throw 'could not identify the grass surface on the primary monitor'
    }
    return $best
}

function Assert-GrassWindowIdentity {
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)] [IntPtr[]] $Expected
    )

    $actual = Get-GrassWindows -Process $Process
    $expectedIds = @($Expected | ForEach-Object { $_.ToInt64() } | Sort-Object)
    $actualIds = @($actual | ForEach-Object { $_.ToInt64() } | Sort-Object)
    if (($expectedIds -join ',') -ne ($actualIds -join ',')) {
        throw "grass HWND set changed; expected [$($expectedIds -join ', ')], actual [$($actualIds -join ', ')]"
    }
}

$process = $null
$grassHwnd = [IntPtr]::Zero
$probeHwnd = [IntPtr]::Zero
$shutdownProbes = @()

try {
    $process = Start-AppForSmoke -ExePath $exePath
    $grassHwnd = Wait-ForWindow `
        -Process $process `
        -ClassName $windowClass `
        -TimeoutSeconds $TimeoutSeconds
    Start-Sleep -Milliseconds 250

    [IntPtr[]]$initialWindows = Get-GrassWindows -Process $process
    if ($initialWindows.Count -eq 0) {
        throw 'Native app created no grass surfaces'
    }

    $primaryBounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $primaryGrass = Find-PrimaryGrassWindow `
        -Windows $initialWindows `
        -PrimaryBounds $primaryBounds
    Wait-ForWindowVisibility `
        -Hwnd $primaryGrass `
        -Visible $true `
        -TimeoutSeconds $TimeoutSeconds

    $otherVisibility = @{}
    foreach ($hwnd in $initialWindows) {
        if ($hwnd -ne $primaryGrass) {
            $otherVisibility[$hwnd.ToInt64()] =
                [DesktopGrass.Smoke.Win32]::IsWindowVisible($hwnd)
        }
    }

    $probeHwnd = New-OpaqueProbeWindow `
        -Title 'DesktopGrass Fullscreen Probe' `
        -Bounds $primaryBounds `
        -Activate
    if ([DesktopGrass.Smoke.Win32]::GetForegroundWindow() -ne $probeHwnd) {
        throw 'fullscreen probe could not become foreground; run on an unlocked interactive desktop'
    }

    Wait-ForWindowVisibility `
        -Hwnd $primaryGrass `
        -Visible $false `
        -TimeoutSeconds $TimeoutSeconds
    foreach ($hwnd in $initialWindows) {
        if ($hwnd -ne $primaryGrass) {
            $expected = [bool]$otherVisibility[$hwnd.ToInt64()]
            $actual = [DesktopGrass.Smoke.Win32]::IsWindowVisible($hwnd)
            if ($actual -ne $expected) {
                throw "fullscreen suppression changed unrelated monitor hwnd=$hwnd"
            }
        }
    }

    Remove-ProbeWindow -Hwnd $probeHwnd
    $probeHwnd = [IntPtr]::Zero
    Wait-ForWindowVisibility `
        -Hwnd $primaryGrass `
        -Visible $true `
        -TimeoutSeconds $TimeoutSeconds
    Assert-GrassWindowIdentity -Process $process -Expected $initialWindows

    $stripBounds = Get-WindowBounds -Hwnd $primaryGrass
    $probeHwnd = New-OpaqueProbeWindow `
        -Title 'DesktopGrass Occlusion Probe' `
        -Bounds $stripBounds `
        -Topmost
    Wait-ForWindowVisibility `
        -Hwnd $primaryGrass `
        -Visible $false `
        -TimeoutSeconds $TimeoutSeconds

    Remove-ProbeWindow -Hwnd $probeHwnd
    $probeHwnd = [IntPtr]::Zero
    Wait-ForWindowVisibility `
        -Hwnd $primaryGrass `
        -Visible $true `
        -TimeoutSeconds $TimeoutSeconds
    Assert-GrassWindowIdentity -Process $process -Expected $initialWindows

    foreach ($hwnd in $initialWindows) {
        $shutdownProbes += New-OpaqueProbeWindow `
            -Title "DesktopGrass Shutdown Probe $($hwnd.ToInt64())" `
            -Bounds (Get-WindowBounds -Hwnd $hwnd) `
            -Topmost
    }
    foreach ($hwnd in $initialWindows) {
        Wait-ForWindowVisibility `
            -Hwnd $hwnd `
            -Visible $false `
            -TimeoutSeconds $TimeoutSeconds
    }

    foreach ($shutdownProbe in $shutdownProbes) {
        Remove-ProbeWindow -Hwnd $shutdownProbe
    }
    $shutdownProbes = @()
    foreach ($hwnd in $initialWindows) {
        $expected = $hwnd -eq $primaryGrass `
            -or [bool]$otherVisibility[$hwnd.ToInt64()]
        Wait-ForWindowVisibility `
            -Hwnd $hwnd `
            -Visible $expected `
            -TimeoutSeconds $TimeoutSeconds
    }
    Assert-GrassWindowIdentity -Process $process -Expected $initialWindows

    foreach ($hwnd in $initialWindows) {
        $shutdownProbes += New-OpaqueProbeWindow `
            -Title "DesktopGrass Shutdown Probe $($hwnd.ToInt64())" `
            -Bounds (Get-WindowBounds -Hwnd $hwnd) `
            -Topmost
    }
    foreach ($hwnd in $initialWindows) {
        Wait-ForWindowVisibility `
            -Hwnd $hwnd `
            -Visible $false `
            -TimeoutSeconds $TimeoutSeconds
    }

    $shutdown = [System.Diagnostics.Stopwatch]::StartNew()
    if (-not [DesktopGrass.Smoke.Win32]::PostMessageW(
            $primaryGrass,
            [DesktopGrass.Smoke.Win32]::WM_CLOSE,
            [IntPtr]::Zero,
            [IntPtr]::Zero)) {
        throw 'failed to post WM_CLOSE while the surface was suppressed'
    }
    if (-not $process.WaitForExit(2000)) {
        throw 'Native app did not exit within 2 seconds while fully suppressed'
    }
    $shutdown.Stop()
    if ($process.ExitCode -ne 0) {
        throw "Native app exited with code $($process.ExitCode) while suppressed"
    }

    Write-Host (
        "PASS Native runtime controls: fullscreen and occlusion reused {0} grass HWND(s); suppressed shutdown completed in {1} ms." `
        -f $initialWindows.Count, $shutdown.ElapsedMilliseconds)
    $global:LASTEXITCODE = 0
}
finally {
    if ($probeHwnd -ne [IntPtr]::Zero) {
        Remove-ProbeWindow -Hwnd $probeHwnd
    }
    foreach ($shutdownProbe in $shutdownProbes) {
        Remove-ProbeWindow -Hwnd $shutdownProbe
    }
    if ($null -ne $process) {
        if (-not $process.HasExited) {
            Stop-AppGracefully `
                -Process $process `
                -Hwnd $grassHwnd `
                -TimeoutSeconds 2
        }
        $process.Dispose()
    }
}
