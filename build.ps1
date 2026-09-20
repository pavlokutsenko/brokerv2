$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = 'C:\tools\dev\.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = 'dotnet'
}
& $dotnet build (Join-Path $repo 'PriceCheck.Collector.sln') -c Release
exit $LASTEXITCODE

