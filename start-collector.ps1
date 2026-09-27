param([string[]]$CollectorArguments = @())
$ErrorActionPreference = 'Stop'
$destination = Join-Path $PSScriptRoot 'release\PriceCheckCollector'
$running = @(Get-Process -Name 'PriceCheck.Collector' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq (Join-Path $destination 'PriceCheck.Collector.exe') })
if ($running.Count) { throw 'Collector is already running; close it before package recovery.' }
& (Join-Path $PSScriptRoot 'scripts\publish-durable.ps1') -Destination $destination -Restore
if ($CollectorArguments.Count) {
    Start-Process -FilePath (Join-Path $destination 'PriceCheck.Collector.exe') -ArgumentList $CollectorArguments -WorkingDirectory $destination
} else {
    Start-Process -FilePath (Join-Path $destination 'PriceCheck.Collector.exe') -WorkingDirectory $destination
}
