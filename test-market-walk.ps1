param(
    [int]$ClientPid = 0,
    [double]$DurationSeconds = 600,
    [string]$ResumeResult = '',
    [switch]$ObstacleCourse,
    [switch]$ReadShops,
    [switch]$SingleTargets,
    [ValidateRange(1,95)][double]$ShopRadius = 85,
    [switch]$PlanOnly,
    [switch]$Stop
)
$ErrorActionPreference = 'Stop'
$TaskRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ($ClientPid -le 0) {
    $TaskClients = @(Get-Process -Name 'lu4.bin' -ErrorAction SilentlyContinue)
    if ($TaskClients.Count -ne 1) { throw 'Specify -ClientPid when there is not exactly one LU4 client.' }
    $ClientPid = $TaskClients[0].Id
}
if ($Stop) {
    $StopFolder = Join-Path $env:LOCALAPPDATA "PriceCheckCollector/research/market-walk/$ClientPid"
    New-Item -ItemType Directory -Force -Path $StopFolder | Out-Null
    New-Item -ItemType File -Force -Path (Join-Path $StopFolder 'STOP') | Out-Null
    Write-Host 'Stop requested.'
    return
}
if (-not $PlanOnly) {
    $OtherWorkers = @(Get-Process -Name BrokerWorker -ErrorAction SilentlyContinue)
    if ($OtherWorkers.Count -gt 0) { throw 'Stop automatic collection before running this walk test.' }
    $OldStop = Join-Path $env:LOCALAPPDATA "PriceCheckCollector/research/market-walk/$ClientPid/STOP"
    Remove-Item -LiteralPath $OldStop -ErrorAction SilentlyContinue
}
$TaskArgs = @((Join-Path $TaskRoot 'tools/WorldGeometry/walk_test.py'), '--pid', "$ClientPid", '--duration', "$DurationSeconds")
if ($ResumeResult) { $TaskArgs += @('--resume-result', $ResumeResult) }
if ($ObstacleCourse) { $TaskArgs += '--obstacle-course' }
if ($ReadShops -or $SingleTargets) { $TaskArgs += @('--read-shops','--shop-radius',"$ShopRadius") }
if ($SingleTargets) { $TaskArgs += '--single-targets' }
if (-not $PlanOnly) { $TaskArgs += '--run' }
& python @TaskArgs
exit $LASTEXITCODE
