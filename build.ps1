param(
    [switch]$ForceBroker,
    [switch]$SkipBroker
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

$release = Join-Path $repo 'release\PriceCheckCollector'
New-Item -ItemType Directory -Force -Path $release | Out-Null
& $dotnet publish (Join-Path $repo 'src\PriceCheck.Collector\PriceCheck.Collector.csproj') `
    -c Release -r win-x64 --self-contained true -o $release --nologo
exit $LASTEXITCODE
