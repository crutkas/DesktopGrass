# Invoke-InteractiveCiSmoke.ps1
#
# Builds and runs one smoke target in a real interactive user session. This is
# the repository-side entry point for the dedicated self-hosted runner.

[CmdletBinding()]
param(
    [ValidateSet('Native', 'Win2D')]
    [string] $Target = 'Native',

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',

    [string] $ArtifactDirectory = 'artifacts\interactive-smoke',

    [switch] $CleanupOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not [IO.Path]::IsPathRooted($ArtifactDirectory)) {
    $ArtifactDirectory = Join-Path $repoRoot $ArtifactDirectory
}
New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null

$tempBase = if ($env:RUNNER_TEMP) {
    $env:RUNNER_TEMP
} else {
    [IO.Path]::GetTempPath()
}
$sandboxRoot = Join-Path $tempBase "DesktopGrass-Smoke-$([guid]::NewGuid().ToString('N'))"
$modulePath = Join-Path $PSScriptRoot 'Smoke.Common.psm1'
$repoPathPrefix = $repoRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

function Stop-WorkspaceApps {
    $names = @('DesktopGrass.Native', 'DesktopGrass.Win2D')
    foreach ($name in $names) {
        foreach ($process in @(Get-Process -Name $name -ErrorAction SilentlyContinue)) {
            $processPath = $null
            try {
                $processPath = $process.Path
            } catch [System.ComponentModel.Win32Exception] {
                Write-Warning "Cannot inspect pid $($process.Id); leaving it untouched."
            }
            if (
                $processPath -and
                [IO.Path]::GetFullPath($processPath).StartsWith(
                    $repoPathPrefix,
                    [StringComparison]::OrdinalIgnoreCase)
            ) {
                Write-Warning "Stopping leftover workspace process $name (pid $($process.Id))."
                Stop-Process -Id $process.Id -Force -ErrorAction Stop
                if (-not $process.WaitForExit(5000)) {
                    throw "workspace process $($process.Id) did not exit during cleanup"
                }
            }
            $process.Dispose()
        }
    }
}

function Remove-SmokeSandboxes {
    Get-ChildItem -LiteralPath $tempBase -Directory -Filter 'DesktopGrass-Smoke-*' -ErrorAction SilentlyContinue |
        ForEach-Object {
            Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Stop
        }
}

function Invoke-LoggedCommand {
    param(
        [Parameter(Mandatory)] [string] $LogName,
        [Parameter(Mandatory)] [scriptblock] $Command
    )

    $logPath = Join-Path $ArtifactDirectory $LogName
    & $Command 2>&1 | Tee-Object -FilePath $logPath
    if ($LASTEXITCODE -ne 0) {
        throw "$LogName command failed with exit code $LASTEXITCODE"
    }
}

function Assert-InteractiveRunner {
    if (
        $env:GITHUB_ACTIONS -eq 'true' -and
        $env:DESKTOPGRASS_INTERACTIVE_RUNNER -ne '1'
    ) {
        throw (
            "Interactive smoke refuses unmarked Actions runners. Provision the " +
            "dedicated self-hosted desktop with DESKTOPGRASS_INTERACTIVE_RUNNER=1.")
    }

    $session = Get-InteractiveSessionState
    $session |
        ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'session.json') -Encoding utf8

    if (
        $session.status -ne 'available' -or
        -not $session.interactive_active -or
        $session.session_id -eq 0
    ) {
        throw (
            "Interactive smoke requires an active, unlocked, non-session-0 " +
            "desktop. Session: $($session | ConvertTo-Json -Compress)")
    }

    Save-DesktopScreenshot `
        -Path (Join-Path $ArtifactDirectory 'desktop-preflight.png') |
        Out-Null
}

if ($CleanupOnly) {
    try {
        Stop-WorkspaceApps
    } finally {
        Remove-SmokeSandboxes
    }
    exit 0
}

$originalEnvironment = @{
    LOCALAPPDATA = $env:LOCALAPPDATA
    APPDATA = $env:APPDATA
    TEMP = $env:TEMP
    TMP = $env:TMP
}
$transcriptStarted = $false

try {
    Start-Transcript `
        -LiteralPath (Join-Path $ArtifactDirectory "$Target-transcript.log") `
        -Force | Out-Null
    $transcriptStarted = $true

    Import-Module $modulePath -Force
    Stop-WorkspaceApps
    Assert-InteractiveRunner

    if ($Target -eq 'Native') {
        if (-not (Get-Command msbuild -ErrorAction SilentlyContinue)) {
            throw 'msbuild is required for Native smoke.'
        }
        $project = Join-Path $repoRoot 'src\DesktopGrass.Native\DesktopGrass.Native.vcxproj'
        Invoke-LoggedCommand -LogName 'Native-build.log' -Command {
            & msbuild $project `
                /p:Configuration=$Configuration `
                /p:Platform=$Platform `
                /m `
                /nologo
        }
    } else {
        if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
            throw 'dotnet is required for managed-reference smoke.'
        }
        $project = Join-Path $repoRoot 'src\DesktopGrass.Win2D\DesktopGrass.Win2D.csproj'
        Invoke-LoggedCommand -LogName 'Win2D-restore.log' -Command {
            & dotnet restore $project
        }
        Invoke-LoggedCommand -LogName 'Win2D-build.log' -Command {
            & dotnet build $project `
                -c $Configuration `
                -p:Platform=$Platform `
                --no-restore `
                --nologo
        }
    }

    $localAppData = Join-Path $sandboxRoot 'LocalAppData'
    $roamingAppData = Join-Path $sandboxRoot 'AppData'
    $tempDirectory = Join-Path $sandboxRoot 'Temp'
    New-Item -ItemType Directory -Path $localAppData, $roamingAppData, $tempDirectory -Force |
        Out-Null
    $env:LOCALAPPDATA = $localAppData
    $env:APPDATA = $roamingAppData
    $env:TEMP = $tempDirectory
    $env:TMP = $tempDirectory

    $smokeArtifacts = Join-Path $ArtifactDirectory $Target
    New-Item -ItemType Directory -Path $smokeArtifacts -Force | Out-Null
    $smokeScript = Join-Path $PSScriptRoot 'Run-SmokeTests.ps1'
    Invoke-LoggedCommand -LogName "$Target-smoke.log" -Command {
        & pwsh `
            -NoLogo `
            -NoProfile `
            -File $smokeScript `
            -Target $Target `
            -Configuration $Configuration `
            -Platform $Platform `
            -TimeoutSeconds 30 `
            -ArtifactDirectory $smokeArtifacts
    }
} finally {
    foreach ($entry in $originalEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
    try {
        Stop-WorkspaceApps
    } finally {
        try {
            if (Test-Path -LiteralPath $sandboxRoot) {
                Remove-Item -LiteralPath $sandboxRoot -Recurse -Force -ErrorAction Stop
            }
        } finally {
            if ($transcriptStarted) {
                Stop-Transcript | Out-Null
            }
        }
    }
}
