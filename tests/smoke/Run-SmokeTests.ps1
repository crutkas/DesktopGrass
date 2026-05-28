# Run-SmokeTests.ps1
#
# Entry point for the DesktopGrass smoke harness. Designed to be invoked
# either directly (`pwsh tests\smoke\Run-SmokeTests.ps1 -Target All`) or via
# GitHub's `winapp ui` CLI as the verification step after `winapp run`.

[CmdletBinding()]
param(
    [ValidateSet('Native','Win2D','WinUI3','All')]
    [string] $Target = 'All',

    [string] $Configuration = 'Release',

    [switch] $ContinueOnFailure
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module "$PSScriptRoot\Smoke.Common.psm1" -Force

# Relative paths follow the build-output convention from the plan:
#   * Native (MSBuild C++):  src\DesktopGrass.Native\out\<Config>\DesktopGrass.Native.exe
#   * Win2D  (.NET):         src\DesktopGrass.Win2D\bin\<Config>\<TFM>\DesktopGrass.Win2D.exe
#   * WinUI3 (.NET):         src\DesktopGrass.WinUI3\bin\<Config>\<TFM>\DesktopGrass.WinUI3.exe
# TFM is resolved lazily at run time so we don't hardcode net8.0-windows10.0.*.
$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path

function Resolve-DotnetExe {
    param(
        [Parameter(Mandatory)] [string] $ProjectDir,
        [Parameter(Mandatory)] [string] $ExeName,
        [Parameter(Mandatory)] [string] $Configuration
    )
    $binConfig = Join-Path $ProjectDir "bin\$Configuration"
    if (-not (Test-Path -LiteralPath $binConfig)) {
        # Return the expected-but-missing path so the error message is useful.
        return (Join-Path $binConfig "<TFM>\$ExeName")
    }
    $tfmDir = Get-ChildItem -LiteralPath $binConfig -Directory -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $tfmDir) {
        return (Join-Path $binConfig "<TFM>\$ExeName")
    }
    return (Join-Path $tfmDir.FullName $ExeName)
}

$Targets = [ordered]@{
    'Native' = @{
        ExePath     = Join-Path $RepoRoot "src\DesktopGrass.Native\out\$Configuration\DesktopGrass.Native.exe"
        WindowClass = 'DesktopGrass.Native.Window'
    }
    'Win2D'  = @{
        ExePath     = Resolve-DotnetExe -ProjectDir (Join-Path $RepoRoot 'src\DesktopGrass.Win2D')  -ExeName 'DesktopGrass.Win2D.exe'  -Configuration $Configuration
        WindowClass = 'DesktopGrass.Win2D.Window'
    }
    'WinUI3' = @{
        ExePath     = Resolve-DotnetExe -ProjectDir (Join-Path $RepoRoot 'src\DesktopGrass.WinUI3') -ExeName 'DesktopGrass.WinUI3.exe' -Configuration $Configuration
        WindowClass = 'DesktopGrass.WinUI3.Window'
    }
}

if ($Target -eq 'All') {
    $selected = $Targets.Keys
} else {
    $selected = @($Target)
}

$results = New-Object System.Collections.Generic.List[object]
$anyFailed = $false

foreach ($name in $selected) {
    $spec = $Targets[$name]
    Write-Host "==> smoke: $name" -ForegroundColor Cyan
    Write-Host "    exe:   $($spec.ExePath)"
    Write-Host "    class: $($spec.WindowClass)"

    $r = Invoke-AppSmoke -ExePath $spec.ExePath -WindowClass $spec.WindowClass

    $results.Add([pscustomobject]@{
        Target       = $name
        Pass         = [bool]$r.Pass
        UniqueColors = [int]$r.UniqueColors
        DurationMs   = [int]$r.DurationMs
        FailReason   = $r.FailReason
    }) | Out-Null

    if (-not $r.Pass) {
        $anyFailed = $true
        Write-Host "    FAIL: $($r.FailReason)" -ForegroundColor Red
        if (-not $ContinueOnFailure) {
            break
        }
    } else {
        Write-Host "    PASS ($($r.UniqueColors) unique colors, $($r.DurationMs) ms)" -ForegroundColor Green
    }
}

Write-Host ''
Write-Host 'Results:' -ForegroundColor Yellow
$results | Format-Table -AutoSize Target, Pass, UniqueColors, DurationMs, FailReason | Out-String | Write-Host

if ($anyFailed) {
    if ($ContinueOnFailure) {
        $global:LASTEXITCODE = 1
        exit 1
    } else {
        $global:LASTEXITCODE = 1
        throw "One or more smoke targets failed."
    }
}

$global:LASTEXITCODE = 0
exit 0
