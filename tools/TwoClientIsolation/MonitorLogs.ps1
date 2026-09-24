# Metadata-only log readers. Partial lines survive until the next poll.
function New-LogCursor([string]$Path) {
    @{ Path = $Path; Offset = 0L; Pending = ''; Lines = 0; LastTx = $null;
       LastRx = $null; WorldOpen = $false; Port = 0; LastClose = $null }
}

function Read-LogLines($Cursor) {
    if (-not [IO.File]::Exists($Cursor.Path)) { throw 'Metadata log missing' }
    $stream = [IO.File]::Open($Cursor.Path, 'Open', 'Read', 'ReadWrite,Delete')
    try {
        $reset = $stream.Length -lt $Cursor.Offset
        if ($reset) {
            $Cursor.Offset = 0L; $Cursor.Pending = ''; $Cursor.Lines = 0
            $Cursor.LastTx = $null; $Cursor.LastRx = $null; $Cursor.WorldOpen = $false
            $Cursor.Port = 0; $Cursor.LastClose = $null
        }
        [void]$stream.Seek($Cursor.Offset, 'Begin')
        $size = [int][Math]::Min(4MB, $stream.Length - $Cursor.Offset)
        $buffer = [byte[]]::new($size)
        $read = $stream.Read($buffer, 0, $size)
        $Cursor.Offset += $read
        $text = $Cursor.Pending + [Text.Encoding]::UTF8.GetString($buffer, 0, $read)
        $parts = $text.Split("`n")
        $Cursor.Pending = $parts[-1]
        $lines = @($parts | Select-Object -SkipLast 1 | ForEach-Object { $_.TrimEnd("`r") })
        $Cursor.Lines += $lines.Count
        return @{ Lines = $lines; Reset = $reset; Backlog = $stream.Length - $Cursor.Offset }
    }
    finally { $stream.Dispose() }
}

function Read-ProxyMetadata($Cursor, [datetimeoffset]$Since) {
    $read = Read-LogLines $Cursor
    $tx = @{}; $rx = @{}; $events = @(); $txBytes = 0L; $rxBytes = 0L
    foreach ($line in $read.Lines) {
        if ($line -notmatch '^([^,]+),([a-z0-9_]+),(-?\d+)$') { continue }
        $stamp = [datetimeoffset]::MinValue
        if (-not [datetimeoffset]::TryParse($Matches[1], [ref]$stamp)) { continue }
        $kind = $Matches[2]; $value = [long]$Matches[3]
        switch ($kind) {
            'destination_port' { $Cursor.Port = $value; $Cursor.WorldOpen = $false }
            'proxy_http_200' { if ($value -eq 7782) { $Cursor.WorldOpen = $true } }
            'client_to_proxy' {
                if ($Cursor.Port -eq 7782) {
                    $Cursor.LastTx = $stamp; $key = [string]$value
                    $tx[$key] = 1 + $tx[$key]; $txBytes += $value
                }
            }
            'proxy_to_client' {
                if ($Cursor.Port -eq 7782) {
                    $Cursor.LastRx = $stamp; $key = [string]$value
                    $rx[$key] = 1 + $rx[$key]; $rxBytes += $value
                }
            }
        }
        if ($kind -in @('client_to_proxy_eof', 'proxy_to_client_eof', 'tunnel_error', 'tunnel_ended')) {
            if ($Cursor.Port -eq 7782) {
                $Cursor.WorldOpen = $false; $Cursor.LastClose = $stamp
                if ($stamp -ge $Since) {
                    $events += @{ utc = $stamp.ToString('o'); kind = $kind; detail = $value }
                }
            }
        }
    }
    @{ tx_sizes = $tx; rx_sizes = $rx; tx_bytes = $txBytes; rx_bytes = $rxBytes;
       close_events = $events; reset = $read.Reset; backlog_bytes = $read.Backlog }
}

function Read-IoctlMetadata($Cursor) {
    $read = Read-LogLines $Cursor
    $codes = @{}; $failures = @()
    foreach ($line in $read.Lines) {
        $fields = $line.Split(',')
        if ($fields.Count -lt 9 -or $fields[1] -notmatch '^[0-9A-Fa-f]{8}$') { continue }
        $key = $fields[1].ToUpperInvariant(); $codes[$key] = 1 + $codes[$key]
        if ($fields[5] -eq '0' -and $fields[6] -match '^\d+$') {
            $failures += @{ utc = $fields[0]; code = $key; last_error = [long]$fields[6] }
        }
    }
    @{ codes = $codes; failures = $failures; lines_seen = $Cursor.Lines;
       cap_reached = ($Cursor.Lines -ge 4097); reset = $read.Reset;
       backlog_bytes = $read.Backlog }
}

function Save-JsonAtomic([string]$Path, $Value) {
    $temporary = $Path + '.tmp'
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporary, $Path, $true)
}

function Add-JsonLine([string]$Path, $Value) {
    $line = ($Value | ConvertTo-Json -Depth 12 -Compress) + "`n"
    [IO.File]::AppendAllText($Path, $line, [Text.UTF8Encoding]::new($false))
}

function Get-AgeSeconds($Timestamp, [datetimeoffset]$Now) {
    if ($null -eq $Timestamp) { return $null }
    [Math]::Round([Math]::Max(0, ($Now - $Timestamp).TotalSeconds), 1)
}
