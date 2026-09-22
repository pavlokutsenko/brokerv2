$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Repository = Split-Path -Parent (Split-Path -Parent $Root)
$Source = Join-Path $Root 'src'
$Stage = Join-Path $Root 'stage'
$Dist = Join-Path $Root 'dist'

Remove-Item -LiteralPath $Stage,$Dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $Stage,$Dist | Out-Null
$Python = 'C:\Users\Pavel\AppData\Local\Programs\Python\Python314\python.exe'
& $Python -m PyInstaller --noconfirm --clean --onedir --name BrokerWorker `
    --distpath $Dist --workpath $Stage --specpath $Stage `
    --add-data "$(Join-Path $Source 'client');client" `
    --add-data "$(Join-Path $Source 'diagnostics');diagnostics" `
    (Join-Path $Source 'broker_worker.py')
if ($LASTEXITCODE -ne 0) { throw 'BrokerWorker build failed' }

$Target = Join-Path $Repository 'src\PriceCheck.Collector\BrokerRuntime'
Remove-Item -LiteralPath $Target -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $Dist 'BrokerWorker') -Destination $Target -Recurse
Remove-Item -LiteralPath $Stage,$Dist -Recurse -Force
Write-Host "Broker runtime staged at $Target"
