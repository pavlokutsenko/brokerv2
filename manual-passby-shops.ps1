param(
    [int]$ClientPid = 0,
    [double]$Radius = 95,
    [double]$DurationMinutes = 0,
    [int]$BatchSize = 64
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Release = Join-Path $Root 'release\PriceCheckCollector'

if ($ClientPid -le 0) {
    $clients = @(Get-Process -Name 'lu4.bin' -ErrorAction SilentlyContinue)
    if ($clients.Count -ne 1) {
        throw "Expected exactly one lu4.bin process; pass -ClientPid explicitly. Found: $($clients.Count)"
    }
    $ClientPid = $clients[0].Id
}

# Stop only collector automation. Force is intentional: the WPF close handler
# owns the game process, while this standalone reader must leave LU4 running.
Get-CimInstance Win32_Process |
    Where-Object {
        $_.Name -eq 'PriceCheck.Collector.exe' -and
        $_.ExecutablePath -eq (Join-Path $Release 'PriceCheck.Collector.exe')
    } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Milliseconds 500

$Runtime = Join-Path $Release "runtime-sessions\$ClientPid\BrokerRuntime"
$Worker = Join-Path $Runtime 'BrokerWorker.exe'
if (-not (Test-Path -LiteralPath $Worker)) {
    throw "Embedded worker for PID $ClientPid was not found. Start the client through Collector once."
}

$OutputFolder = Join-Path $Release 'manual-captures'
New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
$Output = Join-Path $OutputFolder ("manual-passby-{0:yyyyMMdd-HHmmss}.json" -f (Get-Date))
$DurationSeconds = if ($DurationMinutes -gt 0) { $DurationMinutes * 60 } else { 0 }

Write-Host "Manual reader started for PID $ClientPid. Radius=$Radius. Move the character yourself."
Write-Host "Press Ctrl+C to stop. Output: $Output"
& $Worker --mode manual-passby --pid $ClientPid --output $Output `
    --radius $Radius --duration $DurationSeconds --batch-size $BatchSize
exit $LASTEXITCODE
