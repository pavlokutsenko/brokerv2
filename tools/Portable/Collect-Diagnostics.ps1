#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$dataRoot = Join-Path $env:LOCALAPPDATA 'PriceCheckCollector'
$run = Join-Path $dataRoot ('support\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
[void][IO.Directory]::CreateDirectory($run)
$logs = Join-Path $dataRoot 'logs'
# Fixed allowlist: no profiles, credentials, packet captures or memory dumps.
foreach ($pattern in @('proxy-tcp-*.csv', 'ioctl-trace-*.csv', 'agent-*.txt', 'world-identity-*.txt', 'driver-bootstrap.log', 'driver-bootstrap.log.process.log')) {
    if (Test-Path -LiteralPath $logs) {
        Get-ChildItem -LiteralPath $logs -File -Filter $pattern | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $run $_.Name)
        }
    }
}
$clients = @(Get-Process -Name 'lu4','lu4.bin','lu4-win64-shipping','PriceCheck.Collector' -ErrorAction SilentlyContinue |
    ForEach-Object { @{ name = $_.ProcessName; pid = $_.Id; start_utc = $_.StartTime.ToUniversalTime().ToString('o') } })
$os = Get-CimInstance Win32_OperatingSystem
$report = @{
    captured_utc = [datetimeoffset]::UtcNow.ToString('o');
    os_caption = $os.Caption; os_version = $os.Version; os_build = $os.BuildNumber;
    os_architecture = $os.OSArchitecture; processes = $clients;
    collector_sha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'PriceCheck.Collector.dll')).Hash;
    agent_sha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'ClientLaunchRuntime\PriceCheck.ClientAgent.dll')).Hash
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'environment.json') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'package-manifest.json') -Destination $run
$archive = $run + '.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($run, $archive)
Write-Output "Diagnostics saved: $archive"
