$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Repository = Split-Path -Parent (Split-Path -Parent $Root)
$Source = Join-Path $Root 'src'
$Stage = Join-Path $Root 'stage'
$Dist = Join-Path $Root 'dist'

function Assert-WorkspaceDirectory([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath($Repository) + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing directory operation outside repository: $resolved"
    }
}
Assert-WorkspaceDirectory $Stage
Assert-WorkspaceDirectory $Dist

Remove-Item -LiteralPath $Stage,$Dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $Stage,$Dist | Out-Null
$Python = 'C:\Users\Pavel\AppData\Local\Programs\Python\Python314\python.exe'
& $Python -m PyInstaller --noconfirm --clean --onedir --name BrokerWorker `
    --distpath $Dist --workpath $Stage --specpath $Stage `
    --add-data "$(Join-Path $Source 'client');client" `
    --add-data "$(Join-Path $Source 'diagnostics');diagnostics" `
    --add-data "$(Join-Path $Repository 'tools\WorldGeometry');navigation" `
    --add-data "$(Join-Path $Repository 'tools\RemotePrices');remote" `
    --add-data "$(Join-Path $Repository 'maps');navigation/maps" `
    --hidden-import shapely --hidden-import shapely.ops --hidden-import shapely.geometry --hidden-import shapely.prepared --hidden-import shapely.wkb --hidden-import numpy `
    (Join-Path $Source 'broker_worker.py')
if ($LASTEXITCODE -ne 0) { throw 'BrokerWorker build failed' }

$Target = Join-Path $Repository 'src\PriceCheck.Collector\BrokerRuntime'
Assert-WorkspaceDirectory $Target
Remove-Item -LiteralPath $Target -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $Dist 'BrokerWorker') -Destination $Target -Recurse
Remove-Item -LiteralPath $Stage,$Dist -Recurse -Force
Write-Host "Broker runtime staged at $Target"
