param([switch]$SyncFromResearch)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Repository = Split-Path -Parent (Split-Path -Parent $Root)
$Research = 'C:\Users\Pavel\Documents\ChatGPT\driver'
$Source = Join-Path $Root 'src'
$Stage = Join-Path $Root 'stage'
$Dist = Join-Path $Root 'dist'

if ($SyncFromResearch) {
    New-Item -ItemType Directory -Force -Path (Join-Path $Source 'client'), (Join-Path $Source 'diagnostics') | Out-Null
    foreach ($name in @('lu4_memory_client.py','lu4_target_controller.py','lu4_target_session.py')) {
        Copy-Item -LiteralPath (Join-Path $Research "client\$name") -Destination (Join-Path $Source "client\$name") -Force
    }
    foreach ($name in @(
        'broker_query.py','collect_full_broker_inventory.py','discover_unreal_globals.py',
        'emulate_herz_lu4_stubs.py','inspect_shop_ufunctions.py','process_event_broker_capture.py',
        'process_event_shop_capture.py','resolve_active64_state.py','resolve_lu4_direct_hook.py',
        'resolve_lu4_session.py','resolve_target_route.py','scan_lu4_actors.py'
    )) {
        Copy-Item -LiteralPath (Join-Path $Research "diagnostics\$name") -Destination (Join-Path $Source "diagnostics\$name") -Force
    }
    Copy-Item -LiteralPath (Join-Path $Research 'diagnostics\herzbot-live-reconstructed.exe') -Destination (Join-Path $Source 'diagnostics\herzbot-live-reconstructed.exe') -Force
}

Remove-Item -LiteralPath $Stage,$Dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $Stage,$Dist | Out-Null
$Python = 'C:\Users\Pavel\AppData\Local\Programs\Python\Python314\python.exe'
$UnicornDll = 'C:\Users\Pavel\AppData\Roaming\Python\Python314\site-packages\unicorn\lib\unicorn.dll'
& $Python -m PyInstaller --noconfirm --clean --onedir --name BrokerWorker `
    --distpath $Dist --workpath $Stage --specpath $Stage `
    --hidden-import pefile --hidden-import capstone --collect-submodules unicorn `
    --add-binary "$UnicornDll;unicorn\lib" `
    --add-data "$(Join-Path $Source 'client');client" `
    --add-data "$(Join-Path $Source 'diagnostics');diagnostics" `
    (Join-Path $Source 'broker_worker.py')
if ($LASTEXITCODE -ne 0) { throw 'BrokerWorker build failed' }

$Target = Join-Path $Repository 'src\PriceCheck.Collector\BrokerRuntime'
Remove-Item -LiteralPath $Target -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $Dist 'BrokerWorker') -Destination $Target -Recurse
Write-Host "Broker runtime staged at $Target"
