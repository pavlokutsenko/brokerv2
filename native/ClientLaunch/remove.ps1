param([Parameter(Mandatory)][string]$GameBinDir)

$ErrorActionPreference = 'Stop'
$directory = (Resolve-Path -LiteralPath $GameBinDir).Path
$target = Join-Path $directory 'version.dll'
if (-not (Test-Path -LiteralPath $target)) { return }
$state = Join-Path $env:LOCALAPPDATA 'PriceCheckCollector\client-launch-deployments.json'
if (-not (Test-Path -LiteralPath $state)) { throw "Deployment record missing: $state" }
$records = Get-Content -LiteralPath $state -Raw | ConvertFrom-Json -AsHashtable
$recordedHash = $records[$target]
if (-not $recordedHash -or $recordedHash -ne (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) {
    throw "The target does not match the collector's deployment record; leaving it untouched: $target"
}
if (Get-Process -Name 'lu4','lu4.bin','lu4-win64-shipping' -ErrorAction SilentlyContinue) {
    throw 'Stop all LU4 clients before removing version.dll.'
}
Remove-Item -LiteralPath $target
$records.Remove($target)
$records | ConvertTo-Json | Set-Content -LiteralPath $state -Encoding utf8
Write-Output "Removed $target"
