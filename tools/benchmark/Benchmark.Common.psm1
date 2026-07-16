# Shared telemetry and statistics helpers for the DesktopGrass Native benchmark.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:SchemaVersion = 2
$script:PdhValidData = 0
$script:PdhNewData = 1
$script:UnknownUInt32 = [uint32]::MaxValue

if (-not ('DesktopGrass.Benchmark.PdhQuery' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DesktopGrass.Benchmark
{
    public sealed class CounterSample
    {
        public string Name { get; set; }
        public double Value { get; set; }
        public uint Status { get; set; }
    }

    public sealed class PdhQuery : IDisposable
    {
        private const uint ErrorSuccess = 0;
        private const uint PdhMoreData = 0x800007D2;
        private const uint PdhFmtDouble = 0x00000200;

        private IntPtr _query;
        private readonly Dictionary<string, IntPtr> _counters =
            new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);

        [StructLayout(LayoutKind.Sequential)]
        private struct PdhFormattedCounterValue
        {
            public uint Status;
            public double Value;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PdhFormattedCounterValueItem
        {
            public IntPtr Name;
            public PdhFormattedCounterValue FormattedValue;
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhOpenQueryW(
            string dataSource,
            IntPtr userData,
            out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhAddEnglishCounterW(
            IntPtr query,
            string fullCounterPath,
            IntPtr userData,
            out IntPtr counter);

        [DllImport("pdh.dll")]
        private static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhGetFormattedCounterArrayW(
            IntPtr counter,
            uint format,
            ref uint bufferSize,
            ref uint itemCount,
            IntPtr itemBuffer);

        [DllImport("pdh.dll")]
        private static extern uint PdhGetFormattedCounterValue(
            IntPtr counter,
            uint format,
            out uint counterType,
            out PdhFormattedCounterValue value);

        [DllImport("pdh.dll")]
        private static extern uint PdhCloseQuery(IntPtr query);

        public PdhQuery()
        {
            uint status = PdhOpenQueryW(null, IntPtr.Zero, out _query);
            if (status != ErrorSuccess)
            {
                throw CreateException("PdhOpenQueryW", status);
            }
        }

        public uint AddCounter(string key, string fullCounterPath)
        {
            if (String.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Counter key is required.", nameof(key));
            }

            IntPtr counter;
            uint status = PdhAddEnglishCounterW(
                _query,
                fullCounterPath,
                IntPtr.Zero,
                out counter);
            if (status == ErrorSuccess)
            {
                _counters.Add(key, counter);
            }
            return status;
        }

        public uint Collect()
        {
            return PdhCollectQueryData(_query);
        }

        public CounterSample[] ReadArray(string key)
        {
            IntPtr counter = GetCounter(key);
            uint bufferSize = 0;
            uint itemCount = 0;
            uint status = PdhGetFormattedCounterArrayW(
                counter,
                PdhFmtDouble,
                ref bufferSize,
                ref itemCount,
                IntPtr.Zero);

            if (status == ErrorSuccess && bufferSize == 0)
            {
                return Array.Empty<CounterSample>();
            }
            if (status != PdhMoreData)
            {
                throw CreateException("PdhGetFormattedCounterArrayW(size)", status);
            }

            for (int attempt = 0; attempt < 3; attempt++)
            {
                IntPtr buffer = Marshal.AllocHGlobal(checked((int)bufferSize));
                try
                {
                    uint suppliedSize = bufferSize;
                    status = PdhGetFormattedCounterArrayW(
                        counter,
                        PdhFmtDouble,
                        ref suppliedSize,
                        ref itemCount,
                        buffer);
                    if (status == PdhMoreData)
                    {
                        bufferSize = suppliedSize;
                        continue;
                    }
                    if (status != ErrorSuccess)
                    {
                        throw CreateException("PdhGetFormattedCounterArrayW(data)", status);
                    }

                    int itemSize = Marshal.SizeOf<PdhFormattedCounterValueItem>();
                    var result = new CounterSample[itemCount];
                    for (int index = 0; index < itemCount; index++)
                    {
                        IntPtr itemPointer = IntPtr.Add(buffer, checked(index * itemSize));
                        var item = Marshal.PtrToStructure<PdhFormattedCounterValueItem>(
                            itemPointer);
                        result[index] = new CounterSample
                        {
                            Name = Marshal.PtrToStringUni(item.Name) ?? String.Empty,
                            Value = item.FormattedValue.Value,
                            Status = item.FormattedValue.Status
                        };
                    }
                    return result;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            throw new InvalidOperationException(
                "PDH counter instances changed too quickly to obtain a stable sample.");
        }

        public CounterSample ReadValue(string key)
        {
            IntPtr counter = GetCounter(key);
            uint counterType;
            PdhFormattedCounterValue value;
            uint status = PdhGetFormattedCounterValue(
                counter,
                PdhFmtDouble,
                out counterType,
                out value);
            if (status != ErrorSuccess)
            {
                throw CreateException("PdhGetFormattedCounterValue", status);
            }

            return new CounterSample
            {
                Name = key,
                Value = value.Value,
                Status = value.Status
            };
        }

        private IntPtr GetCounter(string key)
        {
            IntPtr counter;
            if (!_counters.TryGetValue(key, out counter))
            {
                throw new KeyNotFoundException("PDH counter is unavailable: " + key);
            }
            return counter;
        }

        private static Exception CreateException(string operation, uint status)
        {
            return new Win32Exception(
                unchecked((int)status),
                String.Format("{0} failed with PDH status 0x{1:X8}.", operation, status));
        }

        public void Dispose()
        {
            if (_query != IntPtr.Zero)
            {
                PdhCloseQuery(_query);
                _query = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        ~PdhQuery()
        {
            Dispose();
        }
    }

    public static class PowerApi
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SystemPowerStatus
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(
            IntPtr userRootPowerKey,
            out IntPtr activePolicyGuid);

        public static SystemPowerStatus ReadSystemPowerStatus()
        {
            SystemPowerStatus status;
            if (!GetSystemPowerStatus(out status))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            return status;
        }

        public static string ReadActiveSchemeGuid()
        {
            IntPtr schemePointer;
            uint result = PowerGetActiveScheme(IntPtr.Zero, out schemePointer);
            if (result != 0)
            {
                throw new Win32Exception(unchecked((int)result));
            }

            try
            {
                return Marshal.PtrToStructure<Guid>(schemePointer).ToString("D");
            }
            finally
            {
                Marshal.FreeHGlobal(schemePointer);
            }
        }
    }
}
'@
}

function Get-BenchmarkSchemaVersion {
    return $script:SchemaVersion
}

function New-MetricStatus {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('available', 'unsupported', 'error', 'partial')]
        [string] $Status,

        [Parameter(Mandatory)]
        [string] $Source,

        [AllowNull()]
        [string] $Reason = $null
    )

    return [ordered]@{
        status = $Status
        source = $Source
        reason = $Reason
    }
}

function Format-PdhStatus {
    param([uint32] $Status)
    return ('0x{0:X8}' -f $Status)
}

function Test-PdhValueStatus {
    param([uint32] $Status)
    return $Status -eq $script:PdhValidData -or $Status -eq $script:PdhNewData
}

function ConvertFrom-GpuEngineInstance {
    param(
        [Parameter(Mandatory)]
        [string] $InstanceName
    )

    $pattern = '^pid_(?<pid>\d+)_luid_(?<luidHigh>0x[0-9a-f]+)_(?<luidLow>0x[0-9a-f]+)_phys_(?<physical>\d+)_eng_(?<engine>\d+)_engtype_(?<engineType>.+)$'
    if ($InstanceName -notmatch $pattern) {
        return [pscustomobject]@{
            parsed           = $false
            instance_name    = $InstanceName
            pid              = $null
            adapter_luid     = $null
            physical_adapter = $null
            engine_index     = $null
            engine_type      = $null
        }
    }

    return [pscustomobject]@{
        parsed           = $true
        instance_name    = $InstanceName
        pid              = [int]$Matches.pid
        adapter_luid     = "$($Matches.luidHigh)_$($Matches.luidLow)"
        physical_adapter = [int]$Matches.physical
        engine_index     = [int]$Matches.engine
        engine_type      = $Matches.engineType
    }
}

function New-BenchmarkCounterSampler {
    $query = [DesktopGrass.Benchmark.PdhQuery]::new()
    $counterSpecs = [ordered]@{
        gpu_engine = @{
            path = '\GPU Engine(*)\Utilization Percentage'
            source = 'PDH \GPU Engine(*)\Utilization Percentage'
            kind = 'array'
        }
        thread_pid = @{
            path = '\Thread(*)\ID Process'
            source = 'PDH \Thread(*)\ID Process'
            kind = 'array'
        }
        thread_context_switches = @{
            path = '\Thread(*)\Context Switches/sec'
            source = 'PDH \Thread(*)\Context Switches/sec'
            kind = 'array'
        }
        system_context_switches = @{
            path = '\System\Context Switches/sec'
            source = 'PDH \System\Context Switches/sec'
            kind = 'value'
        }
        processor_queue_length = @{
            path = '\System\Processor Queue Length'
            source = 'PDH \System\Processor Queue Length'
            kind = 'value'
        }
        system_interrupts = @{
            path = '\Processor Information(_Total)\Interrupts/sec'
            source = 'PDH \Processor Information(_Total)\Interrupts/sec'
            kind = 'value'
        }
        system_dpc_rate = @{
            path = '\Processor Information(_Total)\DPC Rate'
            source = 'PDH \Processor Information(_Total)\DPC Rate'
            kind = 'value'
        }
        energy_meter_power = @{
            path = '\Energy Meter(*)\Power'
            source = 'PDH \Energy Meter(*)\Power'
            kind = 'array'
        }
    }

    $statuses = [ordered]@{}
    $availableCount = 0
    foreach ($entry in $counterSpecs.GetEnumerator()) {
        $code = $query.AddCounter($entry.Key, $entry.Value.path)
        if ($code -eq 0) {
            $statuses[$entry.Key] = New-MetricStatus `
                -Status available `
                -Source $entry.Value.source
            $availableCount++
        } else {
            $statuses[$entry.Key] = New-MetricStatus `
                -Status unsupported `
                -Source $entry.Value.source `
                -Reason ("PDH counter unavailable ({0})" -f (Format-PdhStatus $code))
        }
    }

    $primeStatus = $null
    if ($availableCount -gt 0) {
        $primeCode = $query.Collect()
        if ($primeCode -eq 0) {
            $primeStatus = New-MetricStatus `
                -Status available `
                -Source 'PdhCollectQueryData'
        } else {
            $primeStatus = New-MetricStatus `
                -Status error `
                -Source 'PdhCollectQueryData' `
                -Reason ("Unable to prime counters ({0})" -f (Format-PdhStatus $primeCode))
        }
    } else {
        $primeStatus = New-MetricStatus `
            -Status unsupported `
            -Source 'PdhCollectQueryData' `
            -Reason 'No requested performance counters are available.'
    }

    return [pscustomobject]@{
        query = $query
        counter_specs = $counterSpecs
        capabilities = $statuses
        prime_status = $primeStatus
        primed_utc = (Get-Date).ToUniversalTime().ToString('o')
    }
}

function Read-PdhArray {
    param(
        [Parameter(Mandatory)] $Sampler,
        [Parameter(Mandatory)] [string] $Key
    )

    if ($Sampler.capabilities[$Key].status -ne 'available') {
        return @()
    }
    return @($Sampler.query.ReadArray($Key))
}

function Read-PdhValue {
    param(
        [Parameter(Mandatory)] $Sampler,
        [Parameter(Mandatory)] [string] $Key
    )

    if ($Sampler.capabilities[$Key].status -ne 'available') {
        return $null
    }

    $sample = $Sampler.query.ReadValue($Key)
    if (-not (Test-PdhValueStatus $sample.Status)) {
        return $null
    }
    return [double]$sample.Value
}

function Get-BenchmarkCounterSample {
    param(
        [Parameter(Mandatory)] $Sampler,
        [Parameter(Mandatory)] [int] $ProcessId
    )

    $collectCode = $Sampler.query.Collect()
    if ($collectCode -ne 0) {
        return [pscustomobject]@{
            status = 'error'
            error = "PdhCollectQueryData failed with $(Format-PdhStatus $collectCode)."
            gpu_status = 'error'
            gpu_busiest_engine_pct = $null
            gpu_engines = @()
            context_switch_status = 'error'
            process_context_switches_per_sec = $null
            system_context_switches_per_sec = $null
            system_interrupts_per_sec = $null
            system_dpc_rate = $null
            processor_queue_length = $null
            energy_meter_status = 'error'
            energy_meters = @()
        }
    }

    $errors = [System.Collections.Generic.List[string]]::new()
    $gpuRows = @()
    $gpuStatus = $Sampler.capabilities.gpu_engine.status
    $gpuBusiest = $null
    if ($gpuStatus -eq 'available') {
        try {
            $gpuRows = @(
                foreach ($sample in (Read-PdhArray -Sampler $Sampler -Key 'gpu_engine')) {
                    if (-not (Test-PdhValueStatus $sample.Status)) {
                        continue
                    }
                    $instance = ConvertFrom-GpuEngineInstance $sample.Name
                    if ($instance.parsed -and $instance.pid -eq $ProcessId) {
                        [pscustomobject]@{
                            instance_name = $instance.instance_name
                            pid = $instance.pid
                            adapter_luid = $instance.adapter_luid
                            physical_adapter = $instance.physical_adapter
                            engine_index = $instance.engine_index
                            engine_type = $instance.engine_type
                            utilization_pct = [double]$sample.Value
                            counter_status = [uint32]$sample.Status
                        }
                    }
                }
            )
            if ($gpuRows.Count -gt 0) {
                $gpuBusiest = [double](($gpuRows.utilization_pct |
                    Measure-Object -Maximum).Maximum)
                $gpuStatus = 'available'
            } else {
                $gpuStatus = 'no_process_instance'
            }
        } catch {
            $gpuStatus = 'error'
            $errors.Add("gpu_engine: $($_.Exception.Message)")
        }
    }

    $contextSwitchStatus = 'unsupported'
    $processContextSwitches = $null
    if ($Sampler.capabilities.thread_pid.status -eq 'available' -and
        $Sampler.capabilities.thread_context_switches.status -eq 'available') {
        try {
            $pidSamples = Read-PdhArray -Sampler $Sampler -Key 'thread_pid'
            $contextSamples = Read-PdhArray `
                -Sampler $Sampler `
                -Key 'thread_context_switches'
            $contextByInstance = @{}
            foreach ($sample in $contextSamples) {
                if (Test-PdhValueStatus $sample.Status) {
                    $contextByInstance[$sample.Name] = [double]$sample.Value
                }
            }

            $matched = @(
                foreach ($sample in $pidSamples) {
                    if (-not (Test-PdhValueStatus $sample.Status)) {
                        continue
                    }
                    if ([int][math]::Round($sample.Value) -eq $ProcessId -and
                        $contextByInstance.ContainsKey($sample.Name)) {
                        [double]$contextByInstance[$sample.Name]
                    }
                }
            )
            if ($matched.Count -gt 0) {
                $processContextSwitches = [double](($matched |
                    Measure-Object -Sum).Sum)
                $contextSwitchStatus = 'available'
            } else {
                $contextSwitchStatus = 'no_process_instance'
            }
        } catch {
            $contextSwitchStatus = 'error'
            $errors.Add("thread_context_switches: $($_.Exception.Message)")
        }
    }

    $energyRows = @()
    $energyStatus = $Sampler.capabilities.energy_meter_power.status
    if ($energyStatus -eq 'available') {
        try {
            $energyRows = @(
                foreach ($sample in (Read-PdhArray `
                    -Sampler $Sampler `
                    -Key 'energy_meter_power')) {
                    if (Test-PdhValueStatus $sample.Status) {
                        [pscustomobject]@{
                            meter = $sample.Name
                            power_mw = [double]$sample.Value
                            counter_status = [uint32]$sample.Status
                        }
                    }
                }
            )
            $energyStatus = if ($energyRows.Count -gt 0) {
                'available'
            } else {
                'no_instance'
            }
        } catch {
            $energyStatus = 'error'
            $errors.Add("energy_meter_power: $($_.Exception.Message)")
        }
    }

    $systemValues = [ordered]@{}
    foreach ($key in @(
        'system_context_switches',
        'system_interrupts',
        'system_dpc_rate',
        'processor_queue_length'
    )) {
        try {
            $systemValues[$key] = Read-PdhValue -Sampler $Sampler -Key $key
        } catch {
            $systemValues[$key] = $null
            $errors.Add("$key`: $($_.Exception.Message)")
        }
    }

    return [pscustomobject]@{
        status = if ($errors.Count -eq 0) { 'available' } else { 'partial' }
        error = if ($errors.Count -eq 0) { $null } else { $errors -join ' | ' }
        gpu_status = $gpuStatus
        gpu_busiest_engine_pct = $gpuBusiest
        gpu_engines = $gpuRows
        context_switch_status = $contextSwitchStatus
        process_context_switches_per_sec = $processContextSwitches
        system_context_switches_per_sec = $systemValues.system_context_switches
        system_interrupts_per_sec = $systemValues.system_interrupts
        system_dpc_rate = $systemValues.system_dpc_rate
        processor_queue_length = $systemValues.processor_queue_length
        energy_meter_status = $energyStatus
        energy_meters = $energyRows
    }
}

function Convert-SystemPowerStatus {
    param(
        [Parameter(Mandatory)]
        [DesktopGrass.Benchmark.PowerApi+SystemPowerStatus] $Status
    )

    $acLineStatus = switch ($Status.ACLineStatus) {
        0 { 'battery' }
        1 { 'ac' }
        default { 'unknown' }
    }
    $batteryPresent = if ($Status.BatteryFlag -eq 255) {
        $null
    } else {
        ($Status.BatteryFlag -band 128) -eq 0
    }
    $batteryPercent = if ($Status.BatteryLifePercent -eq 255) {
        $null
    } else {
        [int]$Status.BatteryLifePercent
    }
    $batteryLifeSeconds = if ($Status.BatteryLifeTime -eq $script:UnknownUInt32) {
        $null
    } else {
        [long]$Status.BatteryLifeTime
    }
    $batteryFullLifeSeconds = if (
        $Status.BatteryFullLifeTime -eq $script:UnknownUInt32
    ) {
        $null
    } else {
        [long]$Status.BatteryFullLifeTime
    }

    return [pscustomobject]@{
        ac_line_status = $acLineStatus
        battery_present = $batteryPresent
        battery_percent = $batteryPercent
        battery_saver = [bool]($Status.SystemStatusFlag -eq 1)
        battery_flag_raw = [int]$Status.BatteryFlag
        battery_life_seconds = $batteryLifeSeconds
        battery_full_life_seconds = $batteryFullLifeSeconds
    }
}

function Convert-BatteryProviderValue {
    param([AllowNull()] $Value)

    if ($null -eq $Value) {
        return $null
    }
    $number = [uint64]$Value
    if ($number -eq $script:UnknownUInt32) {
        return $null
    }
    return [long]$number
}

function Get-BenchmarkPowerSnapshot {
    $errors = [System.Collections.Generic.List[string]]::new()
    $systemPower = $null
    try {
        $rawStatus =
            [DesktopGrass.Benchmark.PowerApi]::ReadSystemPowerStatus()
        $systemPower = Convert-SystemPowerStatus $rawStatus
    } catch {
        $errors.Add("GetSystemPowerStatus: $($_.Exception.Message)")
    }

    $activeSchemeGuid = $null
    try {
        $activeSchemeGuid =
            [DesktopGrass.Benchmark.PowerApi]::ReadActiveSchemeGuid()
    } catch {
        $errors.Add("PowerGetActiveScheme: $($_.Exception.Message)")
    }

    $batteryStatus = 'unsupported'
    $batteries = @()
    try {
        $batteryInstances = @(
            Get-CimInstance `
                -Namespace 'root\wmi' `
                -ClassName 'BatteryStatus' `
                -ErrorAction Stop |
                Where-Object { $_.Active }
        )
        if ($batteryInstances.Count -gt 0) {
            $batteries = @(
                foreach ($battery in $batteryInstances) {
                    [pscustomobject]@{
                        instance_name = [string]$battery.InstanceName
                        power_online = [bool]$battery.PowerOnline
                        charging = [bool]$battery.Charging
                        discharging = [bool]$battery.Discharging
                        critical = [bool]$battery.Critical
                        remaining_capacity_mwh =
                            Convert-BatteryProviderValue `
                                $battery.RemainingCapacity
                        charge_rate_mw =
                            Convert-BatteryProviderValue $battery.ChargeRate
                        discharge_rate_mw =
                            Convert-BatteryProviderValue $battery.DischargeRate
                        voltage_mv =
                            Convert-BatteryProviderValue $battery.Voltage
                    }
                }
            )
            $batteryStatus = 'available'
        } elseif ($systemPower -and $systemPower.battery_present -eq $false) {
            $batteryStatus = 'unsupported'
        } else {
            $batteryStatus = 'no_instance'
        }
    } catch {
        $batteryStatus = 'error'
        $errors.Add("BatteryStatus WMI: $($_.Exception.Message)")
    }

    $remainingValues = @(
        $batteries |
            Where-Object { $null -ne $_.remaining_capacity_mwh } |
            ForEach-Object { [double]$_.remaining_capacity_mwh }
    )
    $chargeRateValues = @(
        $batteries |
            Where-Object { $null -ne $_.charge_rate_mw } |
            ForEach-Object { [double]$_.charge_rate_mw }
    )
    $dischargeRateValues = @(
        $batteries |
            Where-Object { $null -ne $_.discharge_rate_mw } |
            ForEach-Object { [double]$_.discharge_rate_mw }
    )

    return [pscustomobject]@{
        status = if ($errors.Count -eq 0) { 'available' } else { 'partial' }
        error = if ($errors.Count -eq 0) { $null } else { $errors -join ' | ' }
        ac_line_status = if ($systemPower) {
            $systemPower.ac_line_status
        } else {
            'unknown'
        }
        battery_present = if ($systemPower) {
            $systemPower.battery_present
        } else {
            $null
        }
        battery_percent = if ($systemPower) {
            $systemPower.battery_percent
        } else {
            $null
        }
        battery_saver = if ($systemPower) {
            $systemPower.battery_saver
        } else {
            $null
        }
        battery_flag_raw = if ($systemPower) {
            $systemPower.battery_flag_raw
        } else {
            $null
        }
        battery_life_seconds = if ($systemPower) {
            $systemPower.battery_life_seconds
        } else {
            $null
        }
        active_power_scheme_guid = $activeSchemeGuid
        battery_telemetry_status = $batteryStatus
        battery_remaining_capacity_mwh = if ($remainingValues.Count -gt 0) {
            [double](($remainingValues | Measure-Object -Sum).Sum)
        } else {
            $null
        }
        battery_charge_rate_mw = if ($chargeRateValues.Count -gt 0) {
            [double](($chargeRateValues | Measure-Object -Sum).Sum)
        } else {
            $null
        }
        battery_discharge_rate_mw = if ($dischargeRateValues.Count -gt 0) {
            [double](($dischargeRateValues | Measure-Object -Sum).Sum)
        } else {
            $null
        }
        batteries = $batteries
    }
}

function Get-BatteryStaticContext {
    $result = [ordered]@{
        status = 'unsupported'
        source = 'root\wmi battery classes'
        reason = $null
        batteries = @()
    }

    try {
        $fullCapacity = @(
            Get-CimInstance `
                -Namespace 'root\wmi' `
                -ClassName 'BatteryFullChargedCapacity' `
                -ErrorAction Stop
        )
        $staticData = @(
            Get-CimInstance `
                -Namespace 'root\wmi' `
                -ClassName 'BatteryStaticData' `
                -ErrorAction Stop
        )

        $staticByInstance = @{}
        foreach ($item in $staticData) {
            $staticByInstance[[string]$item.InstanceName] = $item
        }
        $result.batteries = @(
            foreach ($item in $fullCapacity) {
                $static = $staticByInstance[[string]$item.InstanceName]
                [pscustomobject]@{
                    instance_name = [string]$item.InstanceName
                    full_charged_capacity_mwh =
                        Convert-BatteryProviderValue `
                            $item.FullChargedCapacity
                    designed_capacity_mwh = if ($static) {
                        Convert-BatteryProviderValue $static.DesignedCapacity
                    } else {
                        $null
                    }
                }
            }
        )
        if ($result.batteries.Count -gt 0) {
            $result.status = 'available'
        } else {
            $result.reason = 'No active battery capacity instances were returned.'
        }
    } catch {
        $result.status = 'error'
        $result.reason = $_.Exception.Message
    }

    return [pscustomobject]$result
}

function Get-BenchmarkMachineContext {
    param(
        [Parameter(Mandatory)] [string] $Exe,
        [Parameter(Mandatory)] [string] $Platform,
        [Parameter(Mandatory)] $CounterSampler,
        [Parameter(Mandatory)] $SweepParameters
    )

    $operatingSystem = Get-CimInstance `
        -ClassName Win32_OperatingSystem `
        -ErrorAction Stop
    $processors = @(
        Get-CimInstance -ClassName Win32_Processor -ErrorAction Stop |
            Select-Object Name, NumberOfCores, NumberOfLogicalProcessors
    )
    $videoControllers = @(
        Get-CimInstance -ClassName Win32_VideoController -ErrorAction Stop |
            Select-Object `
                Name,
                DriverVersion,
                PNPDeviceID,
                CurrentHorizontalResolution,
                CurrentVerticalResolution,
                CurrentRefreshRate
    )
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    $isElevated = $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator
    )
    $power = Get-BenchmarkPowerSnapshot
    $batteryStatic = Get-BatteryStaticContext

    $tools = [ordered]@{}
    foreach ($name in @(
        'powercfg.exe',
        'wpr.exe',
        'wpa.exe',
        'wpaexporter.exe',
        'PresentMon.exe',
        'typeperf.exe'
    )) {
        $command = Get-Command $name -ErrorAction SilentlyContinue
        $tools[$name] = [ordered]@{
            available = $null -ne $command
            path = if ($command) { $command.Source } else { $null }
        }
    }

    return [ordered]@{
        schema_version = $script:SchemaVersion
        host_name = $env:COMPUTERNAME
        utc_start = (Get-Date).ToUniversalTime().ToString('o')
        os = [ordered]@{
            caption = [string]$operatingSystem.Caption
            version = [string]$operatingSystem.Version
            build_number = [string]$operatingSystem.BuildNumber
            architecture = [string]$operatingSystem.OSArchitecture
        }
        process_architecture =
            [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        platform = $Platform
        is_elevated = $isElevated
        processors = $processors
        logical_cpus = [Environment]::ProcessorCount
        video_controllers = $videoControllers
        executable = [ordered]@{
            path = $Exe
            sha256 = (Get-FileHash -LiteralPath $Exe -Algorithm SHA256).Hash
        }
        power_at_start = $power
        battery_static = $batteryStatic
        counter_capabilities = $CounterSampler.capabilities
        counter_prime_status = $CounterSampler.prime_status
        tools = $tools
        sweep = $SweepParameters
    }
}

function Get-Mean {
    param([AllowNull()] [object[]] $Values)
    $valid = @($Values | Where-Object { $null -ne $_ })
    if ($valid.Count -eq 0) {
        return $null
    }
    return [double](($valid | Measure-Object -Average).Average)
}

function Get-Percentile {
    param(
        [AllowNull()] [object[]] $Values,
        [Parameter(Mandatory)] [ValidateRange(0, 100)] [double] $Percentile
    )

    $valid = @(
        $Values |
            Where-Object { $null -ne $_ } |
            ForEach-Object { [double]$_ } |
            Sort-Object
    )
    if ($valid.Count -eq 0) {
        return $null
    }
    $index = [math]::Min(
        $valid.Count - 1,
        [int][math]::Floor(
            $Percentile * ($valid.Count - 1) / 100.0 + 0.5
        )
    )
    return [double]$valid[$index]
}

function Get-StandardDeviation {
    param([AllowNull()] [object[]] $Values)
    $valid = @(
        $Values |
            Where-Object { $null -ne $_ } |
            ForEach-Object { [double]$_ }
    )
    if ($valid.Count -lt 2) {
        if ($valid.Count -eq 1) {
            return 0.0
        }
        return $null
    }

    $mean = Get-Mean $valid
    $sumSquares = 0.0
    foreach ($value in $valid) {
        $sumSquares += ($value - $mean) * ($value - $mean)
    }
    return [math]::Sqrt($sumSquares / ($valid.Count - 1))
}

function Write-CsvWithHeader {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string[]] $Columns,
        [AllowNull()] [object[]] $Rows
    )

    $items = @($Rows)
    if ($items.Count -gt 0) {
        $items |
            Select-Object -Property $Columns |
            Export-Csv -LiteralPath $Path -NoTypeInformation -Encoding utf8
        return
    }

    ($Columns -join ',') |
        Out-File -LiteralPath $Path -Encoding utf8
}

Export-ModuleMember -Function @(
    'ConvertFrom-GpuEngineInstance',
    'Get-BatteryStaticContext',
    'Get-BenchmarkCounterSample',
    'Get-BenchmarkMachineContext',
    'Get-BenchmarkPowerSnapshot',
    'Get-BenchmarkSchemaVersion',
    'Get-Mean',
    'Get-Percentile',
    'Get-StandardDeviation',
    'New-BenchmarkCounterSampler',
    'New-MetricStatus',
    'Write-CsvWithHeader'
)
