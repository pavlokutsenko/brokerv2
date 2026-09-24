#requires -Version 7.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\tools\TwoClientIsolation\MonitorLogs.ps1')
function Assert-Monitor([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
$directory = Join-Path $env:TEMP ('PriceCheck-monitor-smoke-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($directory)
try {
    $path = Join-Path $directory 'proxy.csv'
    $cursor = New-LogCursor $path
    [IO.File]::WriteAllText($path, @'
2026-09-24T01:00:00Z,destination_port,2108
2026-09-24T01:00:01Z,proxy_http_200,2108
2026-09-24T01:00:02Z,client_to_proxy_eof,0
2026-09-24T01:00:03Z,destination_port,7782
2026-09-24T01:00:04Z,proxy_http_200,7782
2026-09-24T01:00:05Z,client_to_proxy,27

'@)
    $delta = Read-ProxyMetadata $cursor ([datetimeoffset]'2026-09-24T01:00:04Z')
    Assert-Monitor $cursor.WorldOpen 'HTTP 200 world tunnel must be recognized'
    Assert-Monitor ($delta.close_events.Count -eq 0) 'Historical login EOF is not a new world close'
    Assert-Monitor ($delta.tx_sizes['27'] -eq 1) 'Send length histogram'
    [IO.File]::AppendAllText($path, '2026-09-24T01:00:06Z,proxy_to_client,3')
    $partial = Read-ProxyMetadata $cursor ([datetimeoffset]'2026-09-24T01:00:04Z')
    Assert-Monitor ($partial.rx_bytes -eq 0) 'Do not consume partial line'
    [IO.File]::AppendAllText($path, "1`n2026-09-24T01:00:07Z,proxy_to_client_eof,0`n")
    $closed = Read-ProxyMetadata $cursor ([datetimeoffset]'2026-09-24T01:00:04Z')
    Assert-Monitor ($closed.rx_bytes -eq 31 -and $closed.close_events.Count -eq 1) 'Completed line and remote EOF'
    Assert-Monitor (-not $cursor.WorldOpen) 'Remote EOF closes world state'
    $empty = Read-ProxyMetadata $cursor ([datetimeoffset]'2026-09-24T01:00:04Z')
    Assert-Monitor ($empty.close_events.Count -eq 0) 'Do not duplicate close events'
    [IO.File]::WriteAllText($path, "2026-09-24T01:00:08Z,destination_port,7782`n")
    $reset = Read-ProxyMetadata $cursor ([datetimeoffset]'2026-09-24T01:00:04Z')
    Assert-Monitor $reset.reset 'Detect log truncation'
    Assert-Monitor ($null -eq $cursor.LastTx -and -not $cursor.WorldOpen) 'Reset stale traffic on truncation'
    $ioPath = Join-Path $directory 'ioctl.csv'
    [IO.File]::WriteAllText($ioPath, "utc,code,input,capacity,returned,success,error,module,rva`n2026-09-24T01:00:09Z,00222160,59,0,0,0,5,clmods64.dll,B68F`n")
    $io = Read-IoctlMetadata (New-LogCursor $ioPath)
    Assert-Monitor ($io.failures.Count -eq 1 -and $io.failures[0].last_error -eq 5) 'Preserve failed IOCTL error code'
    Assert-Monitor (-not $io.cap_reached) 'Short trace must not claim the cap'
    Write-Output 'Monitor metadata smoke passed: CONNECT, historical EOF, partial reads, close, truncation, IOCTL failure.'
}
finally {
    # Delete only these two known temporary files, then their empty directory.
    foreach ($name in @('proxy.csv', 'ioctl.csv')) {
        $temporary = Join-Path $directory $name
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
    }
    [IO.Directory]::Delete($directory)
}
