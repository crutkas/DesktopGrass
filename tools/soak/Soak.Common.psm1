Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Initialize-SoakNativeMethods {
    if ('DesktopGrass.Soak.NativeMethods' -as [type]) {
        return
    }

    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DesktopGrass.Soak
{
    public static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern uint GetGuiResources(IntPtr process, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWaitableTimerW(
            IntPtr attributes, bool manualReset, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetWaitableTimer(
            IntPtr timer,
            ref long dueTime,
            int period,
            IntPtr completionRoutine,
            IntPtr argument,
            bool resume);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelWaitableTimer(IntPtr timer);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern bool SetSuspendState(
            bool hibernate, bool forceCritical, bool disableWakeEvent);

        public static void SuspendWithWakeTimer(int wakeAfterSeconds)
        {
            IntPtr timer = CreateWaitableTimerW(IntPtr.Zero, true, null);
            if (timer == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                long dueTime = -checked((long)wakeAfterSeconds * 10000000L);
                if (!SetWaitableTimer(
                    timer,
                    ref dueTime,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    true))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                if (!SetSuspendState(false, false, false))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                CancelWaitableTimer(timer);
                CloseHandle(timer);
            }
        }
    }
}
'@
}

function Get-SoakSchedule {
    param(
        [Parameter(Mandatory)] [int] $DurationSec,
        [Parameter(Mandatory)] [Collections.IDictionary] $Intervals
    )

    $events = [Collections.Generic.List[object]]::new()
    foreach ($entry in $Intervals.GetEnumerator()) {
        $interval = [int]$entry.Value
        if ($interval -le 0) {
            continue
        }
        for ($at = $interval; $at -lt $DurationSec; $at += $interval) {
            $events.Add([pscustomobject]@{
                at_sec = $at
                operation = [string]$entry.Key
            })
        }
    }
    return @(
        $events |
            Sort-Object at_sec, operation
    )
}

function Get-SoakGuiResources {
    param([Parameter(Mandatory)] [Diagnostics.Process] $Process)

    Initialize-SoakNativeMethods
    return [pscustomobject]@{
        gdi_objects = [DesktopGrass.Soak.NativeMethods]::GetGuiResources(
            $Process.Handle, 0)
        user_objects = [DesktopGrass.Soak.NativeMethods]::GetGuiResources(
            $Process.Handle, 1)
    }
}

function Invoke-SoakLoggedProcess {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [Parameter(Mandatory)] [string] $LogPath,
        [Parameter(Mandatory)] [ValidateRange(1, 3600)] [int] $TimeoutSec
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Unable to start '$FilePath'."
        }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit($TimeoutSec * 1000)
        if ($timedOut -and -not $process.HasExited) {
            $process.Kill($true)
        }
        if (-not $process.WaitForExit(5000)) {
            throw "Process $($process.Id) did not terminate after timeout cleanup."
        }
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        @(
            if ($stdout) { $stdout.TrimEnd() }
            if ($stderr) { $stderr.TrimEnd() }
        ) | Out-File -LiteralPath $LogPath -Encoding utf8
        return [pscustomobject]@{
            timed_out = $timedOut
            exit_code = if ($timedOut) { $null } else { $process.ExitCode }
            output = "$stdout`n$stderr"
        }
    } finally {
        $process.Dispose()
    }
}

function Invoke-WakeableSuspend {
    param([Parameter(Mandatory)] [ValidateRange(5, 3600)] [int] $WakeAfterSeconds)

    Initialize-SoakNativeMethods
    [DesktopGrass.Soak.NativeMethods]::SuspendWithWakeTimer($WakeAfterSeconds)
}

function Test-SoakWindowResponsive {
    param(
        [Parameter(Mandatory)] [IntPtr] $Hwnd,
        [ValidateRange(100, 600000)] [int] $TimeoutMilliseconds = 2000
    )

    if ($Hwnd -eq [IntPtr]::Zero) {
        return $false
    }
    $result = [IntPtr]::Zero
    $sendResult = [DesktopGrass.Smoke.Win32]::SendMessageTimeoutW(
        $Hwnd,
        0,
        [IntPtr]::Zero,
        [IntPtr]::Zero,
        2,
        [uint32]$TimeoutMilliseconds,
        [ref]$result)
    return $sendResult -ne [IntPtr]::Zero
}

function Send-SoakWindowCommand {
    param(
        [Parameter(Mandatory)] [IntPtr] $Hwnd,
        [Parameter(Mandatory)] [int] $CommandId,
        [ValidateRange(100, 600000)] [int] $TimeoutMilliseconds = 2000
    )

    $result = [IntPtr]::Zero
    $sendResult = [DesktopGrass.Smoke.Win32]::SendMessageTimeoutW(
        $Hwnd,
        0x0111,
        [IntPtr]$CommandId,
        [IntPtr]::Zero,
        2,
        [uint32]$TimeoutMilliseconds,
        [ref]$result)
    if ($sendResult -eq [IntPtr]::Zero) {
        $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        throw (
            "SendMessageTimeoutW(WM_COMMAND) failed for command $CommandId " +
            "(Win32 error $errorCode).")
    }
}

function Get-SoakDisplayTopologyFingerprint {
    param([Parameter(Mandatory)] [pscustomobject] $DisplayContext)

    if ($DisplayContext.status -ne 'available') {
        return [pscustomobject]@{
            status = $DisplayContext.status
            reason = $DisplayContext.reason
            hash = $null
        }
    }
    try {
        $decodeEdidText = {
            param([AllowNull()] $Value)
            if ($null -eq $Value) {
                return $null
            }
            return -join @(
                $Value |
                    Where-Object { [int]$_ -gt 0 } |
                    ForEach-Object { [char][int]$_ }
            )
        }
        $activeMonitorIds = @(
            Get-CimInstance `
                -Namespace root/WMI `
                -ClassName WmiMonitorID `
                -ErrorAction Stop |
                Where-Object Active |
                Sort-Object InstanceName |
                ForEach-Object {
                    [ordered]@{
                        instance = $_.InstanceName
                        manufacturer = & $decodeEdidText $_.ManufacturerName
                        product_code = & $decodeEdidText $_.ProductCodeID
                        serial_number = & $decodeEdidText $_.SerialNumberID
                        manufacture_year = $_.YearOfManufacture
                        manufacture_week = $_.WeekOfManufacture
                    }
                }
        )
        $desktopMonitors = @(
            Get-CimInstance Win32_DesktopMonitor -ErrorAction Stop |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_.PNPDeviceID) } |
                Sort-Object PNPDeviceID |
                ForEach-Object {
                    [ordered]@{
                        device_id = $_.DeviceID
                        pnp_device_id = $_.PNPDeviceID
                        width = $_.ScreenWidth
                        height = $_.ScreenHeight
                        status = $_.Status
                    }
                }
        )
        $payload = [ordered]@{
            screens = $DisplayContext.details.screens
            video_modes = $DisplayContext.details.video_modes
            active_monitor_identities = $activeMonitorIds
            desktop_monitor_paths = $desktopMonitors
        } | ConvertTo-Json -Depth 8 -Compress
    } catch {
        return [pscustomobject]@{
            status = 'error'
            reason = "Unable to fingerprint monitor identities: $($_.Exception.Message)"
            hash = $null
        }
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($payload)
        $hash = ([Convert]::ToHexString(
            $sha.ComputeHash($bytes))).ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
    return [pscustomobject]@{
        status = 'available'
        reason = $null
        hash = $hash
    }
}

function Test-SoakDisplayContextChanged {
    param(
        [Parameter(Mandatory)] [pscustomobject] $Baseline,
        [Parameter(Mandatory)] [pscustomobject] $Current
    )

    $restored = Test-SoakDisplayContextRestored `
        -Baseline $Baseline `
        -Current $Current
    return [pscustomobject]@{
        pass = (
            $Baseline.status -eq 'available' -and
            $Current.status -eq 'available' -and
            -not $restored.pass
        )
        reason = if ($Baseline.status -ne 'available') {
            "Baseline display context is '$($Baseline.status)': $($Baseline.reason)"
        } elseif ($Current.status -ne 'available') {
            "Current display context is '$($Current.status)': $($Current.reason)"
        } elseif ($restored.pass) {
            'Monitor churn script did not change the active display topology.'
        } else {
            $null
        }
    }
}

function Test-SoakDisplayContextRestored {
    param(
        [Parameter(Mandatory)] [pscustomobject] $Baseline,
        [Parameter(Mandatory)] [pscustomobject] $Current
    )

    if ($Baseline.status -ne 'available') {
        return [pscustomobject]@{
            pass = $false
            reason = "Baseline display context is '$($Baseline.status)': $($Baseline.reason)"
        }
    }
    if ($Current.status -ne 'available') {
        return [pscustomobject]@{
            pass = $false
            reason = "Current display context is '$($Current.status)': $($Current.reason)"
        }
    }
    if (-not [string]::Equals(
        [string]$Baseline.hash,
        [string]$Current.hash,
        [StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject]@{
            pass = $false
            reason = (
                "Display context was not restored (baseline=$($Baseline.hash), " +
                "current=$($Current.hash)).")
        }
    }
    return [pscustomobject]@{
        pass = $true
        reason = $null
    }
}

function Measure-SoakResourceBudget {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Samples,
        [Parameter(Mandatory)] [Collections.IDictionary] $Thresholds
    )

    $checks = [Collections.Generic.List[object]]::new()
    $resourceMetrics = [ordered]@{
        working_set_mb = 'max_working_set_growth_mb'
        private_mb = 'max_private_growth_mb'
        handles = 'max_handle_growth'
        threads = 'max_thread_growth'
        user_objects = 'max_user_object_growth'
        gdi_objects = 'max_gdi_object_growth'
    }

    foreach ($metric in $resourceMetrics.GetEnumerator()) {
        $thresholdName = $metric.Value
        $threshold = [double]$Thresholds[$thresholdName]
        $groups = @(
            $Samples |
                Where-Object {
                    $null -ne $_.PSObject.Properties[$metric.Key] -and
                    $null -ne $_.($metric.Key)
                } |
                Group-Object generation
        )
        $firstBaseline = $null
        $maxWithinGenerationGrowth = 0.0
        $maxGenerationBaselineGrowth = 0.0
        foreach ($group in $groups) {
            $ordered = @($group.Group | Sort-Object elapsed_sec)
            if ($ordered.Count -eq 0) {
                continue
            }
            $baseline = [double]$ordered[0].($metric.Key)
            if ($null -eq $firstBaseline) {
                $firstBaseline = $baseline
            }
            $peak = [double]((
                $ordered |
                    ForEach-Object { [double]$_.$($metric.Key) } |
                    Measure-Object -Maximum
            ).Maximum)
            $maxWithinGenerationGrowth = [Math]::Max(
                $maxWithinGenerationGrowth,
                $peak - $baseline)
            $maxGenerationBaselineGrowth = [Math]::Max(
                $maxGenerationBaselineGrowth,
                $baseline - [double]$firstBaseline)
        }
        $observed = [Math]::Max(
            $maxWithinGenerationGrowth,
            $maxGenerationBaselineGrowth)
        $checks.Add([pscustomobject]@{
            metric = $metric.Key
            kind = 'growth'
            observed = [Math]::Round($observed, 3)
            threshold = $threshold
            pass = $groups.Count -gt 0 -and $observed -le $threshold
        })
    }

    $cpuValues = @(
        $Samples |
            Where-Object { $null -ne $_.cpu_core_pct } |
            ForEach-Object { [double]$_.cpu_core_pct }
    )
    $meanCpu = if ($cpuValues.Count -gt 0) {
        [double](($cpuValues | Measure-Object -Average).Average)
    } else {
        $null
    }
    $maxMeanCpu = [double]$Thresholds.max_mean_cpu_core_pct
    $checks.Add([pscustomobject]@{
        metric = 'cpu_core_pct'
        kind = 'mean'
        observed = if ($null -eq $meanCpu) {
            $null
        } else {
            [Math]::Round($meanCpu, 3)
        }
        threshold = $maxMeanCpu
        pass = $null -ne $meanCpu -and $meanCpu -le $maxMeanCpu
    })

    $unhealthy = @($Samples | Where-Object { -not [bool]$_.healthy })
    $checks.Add([pscustomobject]@{
        metric = 'sample_health'
        kind = 'count'
        observed = $unhealthy.Count
        threshold = 0
        pass = $unhealthy.Count -eq 0
    })

    return [pscustomobject]@{
        pass = @($checks | Where-Object { -not $_.pass }).Count -eq 0
        checks = @($checks)
    }
}

function Get-SoakQualification {
    param(
        [Parameter(Mandatory)] [int] $DurationSec,
        [Parameter(Mandatory)] [int] $MinimumDurationSec,
        [Parameter(Mandatory)] [Collections.IDictionary] $Coverage,
        [Parameter(Mandatory)] [pscustomobject] $ResourceBudget,
        [Parameter(Mandatory)] [bool] $RuntimeHealthy,
        [Parameter(Mandatory)] [bool] $DiagnosticRun
    )

    $criteria = [ordered]@{
        not_diagnostic = -not $DiagnosticRun
        duration = $DurationSec -ge $MinimumDurationSec
        all_scenes = [int]$Coverage.scene_changes -ge 5
        lifecycle = [int]$Coverage.lifecycle_cycles -ge 1
        device_loss = [int]$Coverage.device_loss_runs -ge 1
        sleep_resume = [int]$Coverage.sleep_resume_cycles -ge 1
        monitor_churn = [int]$Coverage.monitor_churn_cycles -ge 1
        sample_coverage = (
            [int]$Coverage.samples -ge [int]$Coverage.minimum_samples
        )
        resource_budget = [bool]$ResourceBudget.pass
        runtime_health = $RuntimeHealthy
    }
    $unmet = @(
        $criteria.GetEnumerator() |
            Where-Object { -not [bool]$_.Value } |
            ForEach-Object { $_.Key }
    )
    $status = if (-not $RuntimeHealthy -or -not $ResourceBudget.pass) {
        'fail'
    } elseif ($unmet.Count -eq 0) {
        'pass'
    } else {
        'not_qualified'
    }
    return [pscustomobject]@{
        status = $status
        all_acceptance_criteria_met = $unmet.Count -eq 0
        criteria = $criteria
        unmet_criteria = $unmet
    }
}

function Write-SoakEvent {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Type,
        [Parameter(Mandatory)] [string] $Status,
        [AllowNull()] [object] $Details
    )

    [ordered]@{
        utc = [DateTime]::UtcNow.ToString('o')
        type = $Type
        status = $Status
        details = $Details
    } |
        ConvertTo-Json -Depth 10 -Compress |
        Add-Content -LiteralPath $Path -Encoding utf8
}

function Write-SoakJson {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [object] $Value
    )

    $Value |
        ConvertTo-Json -Depth 16 |
        Out-File -LiteralPath $Path -Encoding utf8
}

Export-ModuleMember -Function @(
    'Get-SoakDisplayTopologyFingerprint',
    'Get-SoakGuiResources',
    'Get-SoakQualification',
    'Get-SoakSchedule',
    'Invoke-SoakLoggedProcess',
    'Invoke-WakeableSuspend',
    'Measure-SoakResourceBudget',
    'Send-SoakWindowCommand',
    'Test-SoakDisplayContextChanged',
    'Test-SoakDisplayContextRestored',
    'Test-SoakWindowResponsive',
    'Write-SoakEvent',
    'Write-SoakJson'
)
