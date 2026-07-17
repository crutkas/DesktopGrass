[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',

    [string] $TestCaseFilter,

    [string] $ResultsDirectory,

    [string] $LogFileName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-PeMachine {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $stream = $null
    $reader = $null
    try {
        $stream = [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::Read)
        $reader = [System.IO.BinaryReader]::new($stream)

        if ($reader.ReadUInt16() -ne 0x5A4D) {
            throw "Not a PE file (missing MZ header): $Path"
        }

        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 0 -or $peOffset -gt ($stream.Length - 6)) {
            throw "Invalid PE header offset in: $Path"
        }

        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) {
            throw "Not a PE file (missing PE signature): $Path"
        }

        switch ($reader.ReadUInt16()) {
            0x8664 { return 'x64' }
            0xAA64 { return 'ARM64' }
            default { return ('0x{0:X4}' -f $_) }
        }
    }
    finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }
        elseif ($null -ne $stream) {
            $stream.Dispose()
        }
    }
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} `
    'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw "Visual Studio locator not found: $vswhere"
}

$vsInstall = & $vswhere -latest -products * -property installationPath |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($vsInstall)) {
    throw 'Visual Studio is required to run the native unit tests.'
}

$vstest = Join-Path $vsInstall `
    'Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe'
if (-not (Test-Path -LiteralPath $vstest)) {
    throw "Visual Studio test runner not found: $vstest"
}

$testDll = Join-Path $PSScriptRoot `
    "out\$Platform\$Configuration\DesktopGrass.Native.Tests.dll"
if (-not (Test-Path -LiteralPath $testDll)) {
    throw "Build the native test project first; test DLL not found: $testDll"
}

$osArchitecture =
    [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
$processArchitecture =
    [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
$vstestMachine = Get-PeMachine -Path $vstest
$testMachine = Get-PeMachine -Path $testDll

Write-Host "OS architecture:       $osArchitecture"
Write-Host "PowerShell architecture: $processArchitecture"
Write-Host "VSTest PE machine:     $vstestMachine"
Write-Host "Test DLL PE machine:   $testMachine"

if ($testMachine -ne $Platform) {
    throw "Test DLL architecture is $testMachine, expected $Platform`: $testDll"
}
if ($Platform -eq 'ARM64') {
    if ($osArchitecture -ne 'Arm64') {
        throw "ARM64 native execution requires an ARM64 OS; detected $osArchitecture."
    }
    if ($vstestMachine -ne 'ARM64') {
        throw "ARM64 native execution requires an ARM64 VSTest runner; detected $vstestMachine."
    }
}

$vstestArgs = @($testDll, "/Platform:$Platform")
if (-not [string]::IsNullOrWhiteSpace($TestCaseFilter)) {
    $vstestArgs += "/TestCaseFilter:$TestCaseFilter"
}
if (-not [string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $resultsPath = [System.IO.Path]::GetFullPath($ResultsDirectory)
    [void](New-Item -ItemType Directory -Path $resultsPath -Force)
    $vstestArgs += "/ResultsDirectory:$resultsPath"
    Write-Host "TRX results directory: $resultsPath"
}
if (-not [string]::IsNullOrWhiteSpace($LogFileName)) {
    $vstestArgs += "/Logger:trx;LogFileName=$LogFileName"
}
elseif (-not [string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $vstestArgs += (
        "/Logger:trx;LogFileName=" +
        "DesktopGrass.Native.Tests.$Platform.$Configuration.trx")
}

& $vstest @vstestArgs
if ($LASTEXITCODE -ne 0) {
    throw "Native unit tests failed with exit code $LASTEXITCODE."
}
