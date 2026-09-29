#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$payload = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$manifest = Get-Content -LiteralPath (Join-Path $payload 'package-manifest.json') -Raw | ConvertFrom-Json
$root = if ($manifest.schema -ge 3) { Split-Path -Parent $payload } else { $payload }
$root = $root.TrimEnd('\') + '\'
if ($manifest.schema -ge 3) {
    if ($manifest.runtime_directory -ne 'runtime' -or (Split-Path -Leaf $payload) -ne 'runtime') { throw 'Invalid runtime directory' }
    $rootFiles = @(Get-ChildItem -LiteralPath $root -File -Force)
    $rootDirectories = @(Get-ChildItem -LiteralPath $root -Directory -Force)
    if ($rootFiles.Count -ne 1 -or $rootFiles[0].Name -ne $manifest.entry_point -or
        $rootDirectories.Count -ne 1 -or $rootDirectories[0].Name -ne 'runtime') { throw 'Unexpected files at package root' }
}
$product = if ($manifest.product) { [string]$manifest.product } else { 'Collector' }
if ($product -notin @('Launcher','Collector')) { throw 'Unsupported package product' }
$seen = @{}
foreach ($file in $manifest.files) {
    $path = [IO.Path]::GetFullPath((Join-Path $root $file.path))
    if (-not $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or $seen.ContainsKey($path)) {
        throw 'Invalid or duplicate manifest path'
    }
    $seen[$path] = $true
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing package file: $($file.path)" }
    $item = Get-Item -LiteralPath $path
    if ($item.Length -ne $file.bytes -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) {
        throw "Package checksum mismatch: $($file.path)"
    }
}
$required = @("PriceCheck.$product.dll","PriceCheck.$product.runtimeconfig.json",
    'PriceCheck.Launching.dll','PriceCheck.Contracts.dll','PriceCheck.Windows.dll',
    'hostfxr.dll','hostpolicy.dll','coreclr.dll','PresentationFramework.dll',
    'ClientLaunchRuntime\PriceCheck.ClientAgent.dll','ClientLaunchRuntime\PriceCheck.ClientLogin.dll',
    'DriverRuntime\lu4_memory_wfp.sys','DriverRuntime\load-driver.ps1','DriverRuntime\kdu.exe','DriverRuntime\drv64.dll','DriverRuntime\Taigei64.dll')
if ($product -eq 'Collector') { $required += @('PriceCheck.Collection.dll','BrokerRuntime\BrokerWorker.exe','BrokerRuntime\_internal\python314.dll','BrokerRuntime\_internal\base_library.zip','Market-History.ps1') }
elseif ((Test-Path -LiteralPath (Join-Path $payload 'PriceCheck.Collection.dll')) -or (Test-Path -LiteralPath (Join-Path $payload 'BrokerRuntime'))) { throw 'Launcher contains collection components' }
if ($manifest.schema -ge 2) {
    $required += 'PriceCheck.Launching.UI.dll'
    if ($manifest.entry_point -ne "PriceCheck.$product.exe") { throw 'Package entry point must be at archive root' }
}
foreach ($relative in $required) {
    $runtime=Get-Item -LiteralPath (Join-Path $payload $relative)
    if($runtime.Length -eq 0){throw "Empty runtime file: $relative"}
}
if (-not (Test-Path -LiteralPath (Join-Path $root "PriceCheck.$product.exe") -PathType Leaf)) { throw 'Missing desktop executable at root' }
$runtimeConfig=Get-Content -LiteralPath (Join-Path $payload "PriceCheck.$product.runtimeconfig.json") -Raw | ConvertFrom-Json
if(-not $runtimeConfig.runtimeOptions.includedFrameworks){throw 'Self-contained desktop runtimeconfig is invalid'}
Write-Output ("Package verified: {0} files, build {1}." -f $manifest.files.Count, $manifest.created_utc)
