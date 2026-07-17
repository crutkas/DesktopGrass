[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

& $vstest $testDll "/Platform:$Platform"
if ($LASTEXITCODE -ne 0) {
    throw "Native unit tests failed with exit code $LASTEXITCODE."
}
