$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$brokerBuild = Join-Path $repo 'tools\BrokerWorker\build.ps1'
$dotnet = 'C:\tools\dev\.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = 'dotnet'
}
& $brokerBuild
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$release = Join-Path $repo 'release\PriceCheckCollector'
New-Item -ItemType Directory -Force -Path $release | Out-Null
& $dotnet publish (Join-Path $repo 'src\PriceCheck.Collector\PriceCheck.Collector.csproj') `
    -c Release -r win-x64 --self-contained true -o $release
exit $LASTEXITCODE
