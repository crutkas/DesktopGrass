Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Import-Module (Join-Path $repoRoot 'tools\soak\Soak.Common.psm1') -Force

$script:passed = 0
$script:failed = 0

function Assert-True {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Name
    )
    if ($Condition) {
        $script:passed++
        Write-Host "PASS $Name" -ForegroundColor Green
    } else {
        $script:failed++
        Write-Host "FAIL $Name" -ForegroundColor Red
    }
}

function New-Sample {
    param(
        [int] $Index,
        [int] $Generation = 1,
        [double] $WorkingSet = 50,
        [double] $Private = 40,
        [int] $Handles = 20,
        [int] $Threads = 4,
        [int] $UserObjects = 3,
        [int] $GdiObjects = 2,
        [double] $Cpu = 5,
        [bool] $Healthy = $true
    )
    [pscustomobject]@{
        elapsed_sec = $Index
        generation = $Generation
        working_set_mb = $WorkingSet
        private_mb = $Private
        handles = $Handles
        threads = $Threads
        user_objects = $UserObjects
        gdi_objects = $GdiObjects
        cpu_core_pct = $Cpu
        healthy = $Healthy
    }
}

$thresholds = [ordered]@{
    max_working_set_growth_mb = 10
    max_private_growth_mb = 10
    max_handle_growth = 5
    max_thread_growth = 2
    max_user_object_growth = 2
    max_gdi_object_growth = 2
    max_mean_cpu_core_pct = 10
}

$fixtureRoot = Join-Path $env:TEMP (
    'DesktopGrass-SoakFixture-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $fixtureRoot | Out-Null
try {
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $successProcess = Invoke-SoakLoggedProcess `
        -FilePath $pwsh `
        -Arguments @('-NoProfile', '-Command', 'Write-Output fixture-success') `
        -LogPath (Join-Path $fixtureRoot 'success.log') `
        -TimeoutSec 5
    Assert-True `
        -Condition (
            -not $successProcess.timed_out -and
            $successProcess.exit_code -eq 0 -and
            $successProcess.output -match 'fixture-success'
        ) `
        -Name 'logged helper captures successful process output'

    $timedProcess = Invoke-SoakLoggedProcess `
        -FilePath $pwsh `
        -Arguments @('-NoProfile', '-Command', 'Start-Sleep -Seconds 5') `
        -LogPath (Join-Path $fixtureRoot 'timeout.log') `
        -TimeoutSec 1
    Assert-True `
        -Condition $timedProcess.timed_out `
        -Name 'logged helper terminates a hung process tree'
} finally {
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$schedule = Get-SoakSchedule `
    -DurationSec 100 `
    -Intervals ([ordered]@{ scene = 20; lifecycle = 50; disabled = 0 })
Assert-True `
    -Condition ($schedule.Count -eq 5) `
    -Name 'schedule emits deterministic events and omits disabled operations'
Assert-True `
    -Condition (
        $schedule[0].operation -eq 'scene' -and
        $schedule[2].at_sec -eq 50 -and
        $schedule[2].operation -eq 'lifecycle'
    ) `
    -Name 'schedule sorts equal-duration operations deterministically'

$passingSamples = @(
    New-Sample -Index 1
    New-Sample -Index 2 -WorkingSet 55 -Private 45 -Handles 22 -Threads 5
    New-Sample -Index 3 -Generation 2 -WorkingSet 52 -Private 42
    New-Sample -Index 4 -Generation 2 -WorkingSet 58 -Private 48 -Handles 23
)
$passingBudget = Measure-SoakResourceBudget `
    -Samples $passingSamples `
    -Thresholds $thresholds
Assert-True -Condition $passingBudget.pass -Name 'bounded fixture passes budgets'

$growthSamples = @(
    New-Sample -Index 1
    New-Sample -Index 2 -WorkingSet 70
)
$growthBudget = Measure-SoakResourceBudget `
    -Samples $growthSamples `
    -Thresholds $thresholds
Assert-True `
    -Condition (-not $growthBudget.pass) `
    -Name 'unbounded working-set fixture fails'
Assert-True `
    -Condition (
        @(
            $growthBudget.checks |
                Where-Object {
                    $_.metric -eq 'working_set_mb' -and -not $_.pass
                }
        ).Count -eq 1
    ) `
    -Name 'working-set failure identifies the violated metric'

$unhealthyBudget = Measure-SoakResourceBudget `
    -Samples @(
        New-Sample -Index 1
        New-Sample -Index 2 -Healthy $false
    ) `
    -Thresholds $thresholds
Assert-True `
    -Condition (-not $unhealthyBudget.pass) `
    -Name 'hang/crash/stale-window fixture fails sample health'
$emptyBudget = Measure-SoakResourceBudget `
    -Samples @() `
    -Thresholds $thresholds
Assert-True `
    -Condition (-not $emptyBudget.pass) `
    -Name 'empty telemetry fails cleanly without hiding the original error'

$fullCoverage = [ordered]@{
    scene_changes = 5
    lifecycle_cycles = 1
    device_loss_runs = 1
    sleep_resume_cycles = 1
    monitor_churn_cycles = 1
    samples = 100
    expected_samples = 100
    minimum_samples = 80
}
$qualified = Get-SoakQualification `
    -DurationSec 14400 `
    -MinimumDurationSec 14400 `
    -Coverage $fullCoverage `
    -ResourceBudget $passingBudget `
    -RuntimeHealthy $true `
    -DiagnosticRun $false
Assert-True `
    -Condition (
        $qualified.status -eq 'pass' -and
        $qualified.all_acceptance_criteria_met
    ) `
    -Name 'full multi-hour fixture qualifies'

$short = Get-SoakQualification `
    -DurationSec 60 `
    -MinimumDurationSec 14400 `
    -Coverage $fullCoverage `
    -ResourceBudget $passingBudget `
    -RuntimeHealthy $true `
    -DiagnosticRun $true
Assert-True `
    -Condition (
        $short.status -eq 'not_qualified' -and
        $short.unmet_criteria -contains 'duration' -and
        $short.unmet_criteria -contains 'not_diagnostic'
    ) `
    -Name 'short diagnostic fixture cannot masquerade as soak evidence'

$displayBaseline = [pscustomobject]@{
    status = 'available'
    reason = $null
    hash = 'abc123'
}
$displayRestored = Test-SoakDisplayContextRestored `
    -Baseline $displayBaseline `
    -Current ([pscustomobject]@{
        status = 'available'
        reason = $null
        hash = 'ABC123'
    })
Assert-True `
    -Condition $displayRestored.pass `
    -Name 'matching display context proves restoration'
$displayChanged = Test-SoakDisplayContextRestored `
    -Baseline $displayBaseline `
    -Current ([pscustomobject]@{
        status = 'available'
        reason = $null
        hash = 'different'
    })
Assert-True `
    -Condition (-not $displayChanged.pass) `
    -Name 'changed display context fails restoration'
$churnChanged = Test-SoakDisplayContextChanged `
    -Baseline $displayBaseline `
    -Current ([pscustomobject]@{
        status = 'available'
        reason = $null
        hash = 'different'
    })
Assert-True `
    -Condition $churnChanged.pass `
    -Name 'changed display context proves monitor churn'
$churnNoOp = Test-SoakDisplayContextChanged `
    -Baseline $displayBaseline `
    -Current $displayBaseline
Assert-True `
    -Condition (-not $churnNoOp.pass) `
    -Name 'no-op monitor churn is rejected'

$missingTransition = [ordered]@{}
foreach ($entry in $fullCoverage.GetEnumerator()) {
    $missingTransition[$entry.Key] = $entry.Value
}
$missingTransition.sleep_resume_cycles = 0
$partial = Get-SoakQualification `
    -DurationSec 14400 `
    -MinimumDurationSec 14400 `
    -Coverage $missingTransition `
    -ResourceBudget $passingBudget `
    -RuntimeHealthy $true `
    -DiagnosticRun $false
Assert-True `
    -Condition (
        $partial.status -eq 'not_qualified' -and
        $partial.unmet_criteria -contains 'sleep_resume'
    ) `
    -Name 'missing destructive transition remains not qualified'

$sparseCoverage = [ordered]@{}
foreach ($entry in $fullCoverage.GetEnumerator()) {
    $sparseCoverage[$entry.Key] = $entry.Value
}
$sparseCoverage.samples = 79
$sparse = Get-SoakQualification `
    -DurationSec 14400 `
    -MinimumDurationSec 14400 `
    -Coverage $sparseCoverage `
    -ResourceBudget $passingBudget `
    -RuntimeHealthy $true `
    -DiagnosticRun $false
Assert-True `
    -Condition (
        $sparse.status -eq 'not_qualified' -and
        $sparse.unmet_criteria -contains 'sample_coverage'
    ) `
    -Name 'sparse telemetry cannot qualify'

Write-Host ''
Write-Host "Soak fixture tests: $script:passed passed, $script:failed failed."
if ($script:failed -ne 0) {
    exit 1
}
exit 0
