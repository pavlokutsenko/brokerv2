param(
    [switch]$ForceBroker,
    [switch]$SkipBroker,
    [string]$OutputDirectory,
    [string]$LauncherOutputDirectory,
    [string]$PackageDirectory,
    [switch]$SkipPackages,
    [string]$BuildLabel = 'desktop-split'
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
        $inputs += Get-ChildItem -LiteralPath (Join-Path $repo 'tools\WorldGeometry'),(Join-Path $repo 'tools\RemotePrices'),(Join-Path $repo 'maps') -Recurse -File
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
    Where-Object { $_.FullName -notmatch '\\build\\' -and $_.Extension -in @('.c', '.cpp', '.h', '.inc', '.def', '.vcxproj') })
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
$launcherRelease = if ([string]::IsNullOrWhiteSpace($LauncherOutputDirectory)) {
    Join-Path $repo 'release\PriceCheckLauncher'
} else { [IO.Path]::GetFullPath($LauncherOutputDirectory, $repo) }
if ([string]::Equals($release.TrimEnd('\'), $launcherRelease.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Launcher and Collector need separate output directories.'
}
$buildId = [guid]::NewGuid().ToString('N')
$outputs = @{}
foreach ($product in @('Launcher','Collector')) {
    $staging = Join-Path $repo ("workspace\publish-$product-$buildId")
    & $dotnet publish (Join-Path $repo "src\PriceCheck.$product\PriceCheck.$product.csproj") `
        -c Release -r win-x64 --self-contained true -o $staging --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $buildInfo = [ordered]@{
        SchemaVersion = 2; Product = $product; BuildLabel = $BuildLabel; BuildId = $buildId
        BuiltAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        AppSha256 = (Get-FileHash -LiteralPath (Join-Path $staging "PriceCheck.$product.dll") -Algorithm SHA256).Hash
    }
    if ($product -eq 'Collector') {
        $buildInfo.CollectorSha256 = $buildInfo.AppSha256
        $buildInfo.CollectionSha256 = (Get-FileHash -LiteralPath (Join-Path $staging 'PriceCheck.Collection.dll') -Algorithm SHA256).Hash
        $buildInfo.BrokerSha256 = (Get-FileHash -LiteralPath (Join-Path $staging 'BrokerRuntime\BrokerWorker.exe') -Algorithm SHA256).Hash
    }
    $buildInfo | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $staging 'build-info.json') -Encoding utf8
    & (Join-Path $repo 'scripts\layout-portable.ps1') -Source $staging -Product $product -Dotnet $dotnet
    $outputs[$product] = $staging
}
# Publish only after both applications compiled. Packages always use fresh output,
# and do not include stale files or runtime state from release directories.
& (Join-Path $repo 'scripts\publish-durable.ps1') -Product Launcher -Source $outputs.Launcher -Destination $launcherRelease
& (Join-Path $repo 'scripts\publish-durable.ps1') -Source $outputs.Collector -Destination $release
if (-not $SkipPackages) {
    foreach ($product in @('Launcher','Collector')) {
        & (Join-Path $repo 'scripts\package-portable.ps1') -Product $product -PublishDirectory $outputs[$product] -PackageDirectory $PackageDirectory
    }
}
Write-Host "Ready: $launcherRelease and $release"
