function Read-Positions([string]$PythonPath, [string[]]$Names) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $PythonPath; $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $info.ArgumentList.Add((Join-Path $PSScriptRoot 'inspect_live_worlds.py'))
    foreach ($name in $Names) { $info.ArgumentList.Add('--profile'); $info.ArgumentList.Add($name) }
    $child = [Diagnostics.Process]::new(); $child.StartInfo = $info
    try {
        [void]$child.Start()
        $output = $child.StandardOutput.ReadToEndAsync()
        $errors = $child.StandardError.ReadToEndAsync()
        if (-not $child.WaitForExit(8000)) {
            $child.Kill($true)
            throw 'Position observer timeout'
        }
        if ($child.ExitCode -notin @(0, 1) -or [string]::IsNullOrWhiteSpace($output.Result)) {
            throw 'Position observer failed'
        }
        # The observer returns only profile/PID/world, a validity flag, and bounded read errors.
        return @($output.Result | ConvertFrom-Json)
    }
    finally { $child.Dispose() }
}

function Get-WorldSample($Bindings, [string]$ProfilePath, [string]$PythonPath, [datetimeoffset]$Started) {
    $now = [datetimeoffset]::UtcNow
    $stored = @(Get-Content -LiteralPath $ProfilePath -Raw | ConvertFrom-Json)
    $tcp = @(Get-NetTCPConnection -ErrorAction Stop)
    $rows = @(); $allBound = $true
    foreach ($binding in $Bindings) {
        $configured = @($stored | Where-Object { $_.Id -eq $binding.Id })
        $bound = $configured.Count -eq 1 -and $configured[0].LastProcessId -eq $binding.Pid
        $process = Get-Process -Id $binding.Pid -ErrorAction SilentlyContinue
        $same = $null -ne $process -and $process.ProcessName -eq 'lu4.bin' -and
            $process.StartTime.ToUniversalTime().Ticks -eq $binding.StartTicks
        if (-not $bound -or -not $same) { $allBound = $false }
        $states = @($tcp | Where-Object { $_.OwningProcess -eq $binding.Pid } |
            Group-Object State | ForEach-Object { @{ state = [string]$_.Name; count = $_.Count } })
        $established = @($tcp | Where-Object { $_.OwningProcess -eq $binding.Pid -and $_.State -eq 'Established' }).Count
        $proxy = $null; $ioctl = $null; $logError = $null
        try { $proxy = Read-ProxyMetadata $binding.Proxy $Started }
        catch { $logError = 'proxy_metadata_unavailable' }
        try { $ioctl = Read-IoctlMetadata $binding.Ioctl }
        catch { if (-not $logError) { $logError = 'ioctl_metadata_unavailable' } }
        $txAge = Get-AgeSeconds $binding.Proxy.LastTx $now
        $rxAge = Get-AgeSeconds $binding.Proxy.LastRx $now
        $state = 'pending_position'
        if (-not $bound) { $state = 'profile_binding_changed' }
        elseif (-not $process) { $state = 'process_exited' }
        elseif (-not $same) { $state = 'pid_reused' }
        elseif ($established -eq 0) { $state = 'no_established_connection' }
        elseif ($logError) { $state = $logError }
        elseif ($proxy.backlog_bytes -gt 0) { $state = 'metadata_backlog' }
        elseif (-not $binding.Proxy.WorldOpen) { $state = 'world_tunnel_closed' }
        elseif ($null -eq $txAge -or $null -eq $rxAge -or $txAge -gt 90 -or $rxAge -gt 90) { $state = 'traffic_quiet' }
        $rows += @{
            profile = $binding.Name; pid = $binding.Pid; state = $state;
            process_matches = $same; binding_matches = $bound; tcp_states = $states;
            established = $established; last_tx_age_seconds = $txAge; last_rx_age_seconds = $rxAge;
            world_tunnel_open = $binding.Proxy.WorldOpen; position_valid = $null;
            proxy_delta = $proxy; ioctl_delta = $ioctl
        }
    }
    $positions = @()
    if ($allBound) {
        try { $positions = @(Read-Positions $PythonPath @($Bindings.Name)) }
        catch { foreach ($row in $rows) { if ($row.state -eq 'pending_position') { $row.state = 'position_observer_error' } } }
    }
    foreach ($row in $rows) {
        $position = @($positions | Where-Object { $_.pid -eq $row.pid })
        if ($position.Count -eq 1) { $row.position_valid = [bool]$position[0].player_position_valid }
        if ($row.state -eq 'pending_position') {
            $row.state = if (-not $allBound) { 'position_check_skipped' }
                elseif ($row.position_valid) { 'healthy' } else { 'position_unavailable' }
        }
    }
    @{ utc = $now.ToString('o'); baseline = $false; clients = $rows;
       pair_healthy = (@($rows | Where-Object { $_.state -ne 'healthy' }).Count -eq 0) }
}
