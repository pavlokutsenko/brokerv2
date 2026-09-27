#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') + '\'
$manifest = Get-Content -LiteralPath (Join-Path $root 'package-manifest.json') -Raw | ConvertFrom-Json
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
$required = @("PriceCheck.$product.exe","PriceCheck.$product.dll","PriceCheck.$product.runtimeconfig.json",
    'PriceCheck.Launching.dll','PriceCheck.Contracts.dll','PriceCheck.Windows.dll',
    'hostfxr.dll','hostpolicy.dll','coreclr.dll','PresentationFramework.dll',
    'ClientLaunchRuntime\PriceCheck.ClientAgent.dll','ClientLaunchRuntime\PriceCheck.ClientLogin.dll',
    'DriverRuntime\lu4_memory_wfp.sys','DriverRuntime\load-driver.ps1','DriverRuntime\kdu.exe','DriverRuntime\drv64.dll','DriverRuntime\Taigei64.dll')
if ($product -eq 'Collector') { $required += @('PriceCheck.Collection.dll','BrokerRuntime\BrokerWorker.exe','BrokerRuntime\_internal\python314.dll','BrokerRuntime\_internal\base_library.zip') }
elseif ((Test-Path -LiteralPath (Join-Path $root 'PriceCheck.Collection.dll')) -or (Test-Path -LiteralPath (Join-Path $root 'BrokerRuntime'))) { throw 'Launcher contains collection components' }
if ($manifest.schema -ge 2) {
    $required += 'PriceCheck.Launching.UI.dll'
    if ($manifest.entry_point -ne "PriceCheck.$product.exe") { throw 'Package entry point must be at archive root' }
}
foreach ($relative in $required) {
    $runtime=Get-Item -LiteralPath (Join-Path $root $relative)
    if($runtime.Length -eq 0){throw "Empty runtime file: $relative"}
}
$runtimeConfig=Get-Content -LiteralPath (Join-Path $root "PriceCheck.$product.runtimeconfig.json") -Raw | ConvertFrom-Json
if(-not $runtimeConfig.runtimeOptions.includedFrameworks){throw 'Self-contained desktop runtimeconfig is invalid'}
Write-Output ("Package verified: {0} files, build {1}." -f $manifest.files.Count, $manifest.created_utc)
