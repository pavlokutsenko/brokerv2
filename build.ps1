param(
    [switch]$ForceBroker,
    [switch]$SkipBroker,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$brokerBuild = Join-Path $repo 'tools\BrokerWorker\build.ps1'
$brokerSource = Join-Path $repo 'tools\BrokerWorker\src'
$brokerRuntime = Join-Path $repo 'src\PriceCheck.Collector\BrokerRuntime\BrokerWorker.exe'
$dotnet = 'C:\tools\dev\.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = 'dotnet'
}

if (-not $SkipBroker) {
    $brokerChanged = $ForceBroker -or -not (Test-Path -LiteralPath $brokerRuntime)
    if (-not $brokerChanged) {
        $runtimeTime = (Get-Item -LiteralPath $brokerRuntime).LastWriteTimeUtc
        $inputs = @(Get-ChildItem -LiteralPath $brokerSource -Recurse -File -Filter '*.py')
        $inputs += Get-Item -LiteralPath $brokerBuild
        $brokerChanged = $null -ne ($inputs | Where-Object { $_.LastWriteTimeUtc -gt $runtimeTime } | Select-Object -First 1)
    }
    if ($brokerChanged) {
        Write-Host 'BrokerWorker: rebuilding changed runtime...'
        & $brokerBuild
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    else {
        Write-Host 'BrokerWorker: unchanged, using staged runtime.'
    }
}

$clientLaunchBuild = Join-Path $repo 'native\ClientLaunch\build.ps1'
$clientLaunchRuntime = Join-Path $repo 'native\ClientLaunch\build\x64\PriceCheck.ClientAgent.dll'
$clientLaunchProxy = Join-Path $repo 'native\ClientLaunch\build\x64\version.dll'
$clientLoginRuntime = Join-Path $repo 'native\ClientLaunch\build\x64\PriceCheck.ClientLogin.dll'
$clientLaunchSources = @(Get-ChildItem -LiteralPath (Join-Path $repo 'native\ClientLaunch') -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\build\\' -and $_.Extension -in @('.c', '.cpp', '.h', '.def', '.vcxproj') })
if (-not (Test-Path -LiteralPath $clientLaunchRuntime) -or -not (Test-Path -LiteralPath $clientLaunchProxy) -or
    -not (Test-Path -LiteralPath $clientLoginRuntime) -or
    ($clientLaunchSources | Where-Object { $_.LastWriteTimeUtc -gt (Get-Item -LiteralPath $clientLaunchRuntime).LastWriteTimeUtc } | Select-Object -First 1)) {
    & $clientLaunchBuild
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$release = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repo 'release\PriceCheckCollector'
} else {
    [System.IO.Path]::GetFullPath($OutputDirectory, $repo)
}
New-Item -ItemType Directory -Force -Path $release | Out-Null
& $dotnet publish (Join-Path $repo 'src\PriceCheck.Collector\PriceCheck.Collector.csproj') `
    -c Release -r win-x64 --self-contained true -o $release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$obsoleteProxy = Join-Path $release 'ClientLaunchRuntime\version.dll'
if (Test-Path -LiteralPath $obsoleteProxy) {
    Remove-Item -LiteralPath $obsoleteProxy
}
exit 0
