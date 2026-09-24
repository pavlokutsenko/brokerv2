#requires -Version 7.0
[CmdletBinding()]
param(
    [string[]]$Profiles = @('Gamma', 'Black'),
    [ValidateRange(5, 120)][int]$IntervalSeconds = 10,
    [ValidateRange(1, 168)][int]$Hours = 24,
    [string]$PythonPath = 'python.exe',
    [string]$RunDirectory,
    [switch]$Once
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'MonitorLogs.ps1')
. (Join-Path $PSScriptRoot 'MonitorSample.ps1')
$dataRoot = Join-Path $env:LOCALAPPDATA 'PriceCheckCollector'
$monitorRoot = Join-Path $dataRoot 'research\long-monitor'
$profilePath = Join-Path $dataRoot 'profiles.json'
$logRoot = Join-Path $dataRoot 'logs'
if (-not $RunDirectory) { $RunDirectory = Join-Path $monitorRoot (Get-Date -Format 'yyyyMMdd-HHmmss-fff') }
$RunDirectory = [IO.Path]::GetFullPath($RunDirectory)
if (-not $RunDirectory.StartsWith($monitorRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'RunDirectory must be inside LocalAppData/PriceCheckCollector/research/long-monitor'
}
$mutex = [Threading.Mutex]::new($false, 'Local\PriceCheckWorldMonitor')
$locked = $false
$status = $null
try {
    if (-not $Once) {
        try { $locked = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked = $true }
        if (-not $locked) { throw 'A world monitor is already running' }
    }
    $saved = @(Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json)
    $bindings = @()
    foreach ($name in $Profiles) {
        $matches = @($saved | Where-Object { $_.Name -eq $name })
        if ($matches.Count -ne 1 -or -not $matches[0].LastProcessId) { throw 'Missing/ambiguous profile binding' }
        $profile = $matches[0]
        $process = Get-Process -Id $profile.LastProcessId -ErrorAction Stop
        if ($process.ProcessName -ne 'lu4.bin') { throw 'Profile is not bound to a LU4 process' }
        $bindings += @{
            Name = $name; Id = $profile.Id; Pid = [int]$process.Id;
            StartTicks = $process.StartTime.ToUniversalTime().Ticks;
            Proxy = (New-LogCursor (Join-Path $logRoot "proxy-tcp-$($process.Id).csv"));
            Ioctl = (New-LogCursor (Join-Path $logRoot "ioctl-trace-$($process.Id).csv"))
        }
    }
    if ($bindings.Count -lt 2 -or @($bindings.Pid | Sort-Object -Unique).Count -ne $bindings.Count) {
        throw 'Monitoring needs at least two distinct live profile PIDs'
    }
    if (Test-Path -LiteralPath (Join-Path $RunDirectory 'status.json')) { throw 'Run directory already contains a monitor run' }
    [void][IO.Directory]::CreateDirectory($RunDirectory)
    $started = [datetimeoffset]::UtcNow; $deadline = $started.AddHours($Hours)
    $status = [ordered]@{
        schema = 1; phase = 'running'; monitor_pid = $PID;
        monitor_process_start_utc = (Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('o');
        started_utc = $started.ToString('o'); deadline_utc = $deadline.ToString('o');
        updated_utc = $started.ToString('o'); interval_seconds = $IntervalSeconds;
        sample_count = 0; healthy_pair_samples = 0; state_event_count = 0;
        gap_count = 0; max_gap_seconds = 0; run_directory = $RunDirectory;
        clients = @($bindings | ForEach-Object { @{ profile = $_.Name; pid = $_.Pid } });
        last_sample = $null; failure_type = $null
    }
    Save-JsonAtomic (Join-Path $RunDirectory 'status.json') $status
    if (-not $Once) { Save-JsonAtomic (Join-Path $monitorRoot 'current.json') @{ run_directory = $RunDirectory; monitor_pid = $PID } }
    $previousSignature = $null; $previousTime = $null; $next = $started
    while ([datetimeoffset]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath (Join-Path $RunDirectory 'stop.request')) { $status.phase = 'stopped'; break }
        try { $sample = Get-WorldSample $bindings $profilePath $PythonPath $started }
        catch {
            # A temporarily unavailable config/OS query must not end a day-long observation.
            $sample = @{ utc = [datetimeoffset]::UtcNow.ToString('o'); baseline = $false;
                pair_healthy = $false; observer_error_type = $_.Exception.GetType().Name;
                clients = @($bindings | ForEach-Object { @{ profile = $_.Name; pid = $_.Pid;
                    state = 'sample_observer_error'; proxy_delta = $null; ioctl_delta = $null } }) }
        }
        $sample.baseline = $status.sample_count -eq 0
        $sampleTime = [datetimeoffset]::Parse($sample.utc)
        $gap = if ($previousTime) { ($sampleTime - $previousTime).TotalSeconds } else { 0 }
        $status.max_gap_seconds = [Math]::Max($status.max_gap_seconds, [Math]::Round($gap, 2))
        if ($gap -gt [Math]::Max(45, 3 * $IntervalSeconds)) {
            $status.gap_count++
            Add-JsonLine (Join-Path $RunDirectory 'events.jsonl') @{ utc = $sample.utc; kind = 'observation_gap'; seconds = $gap }
        }
        $signature = ($sample.clients | ForEach-Object { $_.profile + ':' + $_.state }) -join '|'
        $newCloses = @($sample.clients | ForEach-Object { $_.proxy_delta.close_events })
        $newFailures = @($sample.clients | ForEach-Object { $_.ioctl_delta.failures })
        $newResets = @($sample.clients | Where-Object { $_.proxy_delta.reset -or $_.ioctl_delta.reset })
        if ($signature -ne $previousSignature -or $newCloses.Count -gt 0 -or $newFailures.Count -gt 0 -or $newResets.Count -gt 0) {
            $status.state_event_count++
            $event = @{ sequence = $status.state_event_count; utc = $sample.utc;
                kind = 'state_or_metadata_change'; baseline = $sample.baseline; sample = $sample }
            Add-JsonLine (Join-Path $RunDirectory 'events.jsonl') $event
            $snapshot = Join-Path $RunDirectory ('event-{0:D4}' -f $status.state_event_count)
            [void][IO.Directory]::CreateDirectory($snapshot)
            Save-JsonAtomic (Join-Path $snapshot 'sample.json') $sample
            foreach ($binding in $bindings) {
                # Only known metadata logs, never credentials or raw capture files.
                foreach ($source in @($binding.Proxy.Path, $binding.Ioctl.Path)) {
                    if (Test-Path -LiteralPath $source) {
                        $target = Join-Path $snapshot ([IO.Path]::GetFileName($source))
                        try { Get-Content -LiteralPath $source -Tail 2000 | Set-Content -LiteralPath $target -Encoding utf8 }
                        catch { Add-JsonLine (Join-Path $RunDirectory 'events.jsonl') @{
                            utc = $sample.utc; kind = 'snapshot_unavailable'; file = [IO.Path]::GetFileName($source) } }
                    }
                }
            }
        }
        Add-JsonLine (Join-Path $RunDirectory 'samples.jsonl') $sample
        $status.sample_count++
        if ($sample.pair_healthy) { $status.healthy_pair_samples++ }
        $status.updated_utc = [datetimeoffset]::UtcNow.ToString('o'); $status.last_sample = $sample
        Save-JsonAtomic (Join-Path $RunDirectory 'status.json') $status
        $previousSignature = $signature; $previousTime = $sampleTime
        if ($Once) { $status.phase = 'completed_once'; break }
        $next = $next.AddSeconds($IntervalSeconds)
        if ($next -lt [datetimeoffset]::UtcNow) { $next = [datetimeoffset]::UtcNow.AddSeconds($IntervalSeconds) }
        $sleepMs = [int][Math]::Max(0, ($next - [datetimeoffset]::UtcNow).TotalMilliseconds)
        if ($sleepMs -gt 0) { Start-Sleep -Milliseconds $sleepMs }
    }
    if ($status.phase -eq 'running') { $status.phase = 'completed' }
}
catch {
    if ($status) { $status.phase = 'failed'; $status.failure_type = $_.Exception.GetType().Name }
    throw
}
finally {
    if ($status) {
        $status.updated_utc = [datetimeoffset]::UtcNow.ToString('o')
        Save-JsonAtomic (Join-Path $RunDirectory 'status.json') $status
    }
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
