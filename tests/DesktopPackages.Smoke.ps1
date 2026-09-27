#requires -Version 7.0
param([Parameter(Mandatory=$true)][string[]]$Archives)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$work = Join-Path $repo ('workspace\desktop-packages-smoke-' + [guid]::NewGuid().ToString('N'))
$shell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
foreach ($archive in $Archives) {
    $archive = [IO.Path]::GetFullPath($archive)
    $expectedHash = (Get-Content -LiteralPath ($archive + '.sha256') -Raw).Split(' ')[0]
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Archive checksum differs' }
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $rootExes = @($zip.Entries | Where-Object { $_.FullName -in @('PriceCheck.Launcher.exe','PriceCheck.Collector.exe') })
        if ($rootExes.Count -ne 1) { throw 'Expected exactly one desktop executable at ZIP root' }
        $product = if ($rootExes[0].Name -eq 'PriceCheck.Launcher.exe') { 'Launcher' } else { 'Collector' }
        if ($zip.Entries | Where-Object { $_.FullName -match '(^|/)(profiles|launch-templates)\.json$|(^|/)(logs|data|runtime-sessions)/' }) { throw 'User state is included in archive' }
        $destination = Join-Path $work $product
        [IO.Compression.ZipFile]::ExtractToDirectory($archive, $destination)
        $manifest = Get-Content -LiteralPath (Join-Path $destination 'package-manifest.json') -Raw | ConvertFrom-Json
        if ($manifest.product -ne $product -or $manifest.entry_point -ne $rootExes[0].Name -or $manifest.includes_server -or $manifest.includes_user_settings) { throw 'Wrong package metadata' }
        if ($zip.Entries.Count -ne $manifest.files.Count + 1) { throw 'Unexpected archive entries outside the manifest' }
    }
    finally { $zip.Dispose() }
    & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $destination 'Verify-Package.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Extracted package does not verify on Windows PowerShell 5.1' }
    $deps = Get-Content -LiteralPath (Join-Path $destination "PriceCheck.$product.deps.json") -Raw
    if ($product -eq 'Launcher' -and $deps -match 'PriceCheck\.(Collection|Collector)/') { throw 'Launcher references collection in runtime dependencies' }
    # Demonstrate that verification rejects a same-length damaged assembly.
    $target = Join-Path $destination "PriceCheck.$product.dll"
    $original = [IO.File]::ReadAllBytes($target); $damaged = [byte[]]$original.Clone(); $damaged[0] = $damaged[0] -bxor 1
    [IO.File]::WriteAllBytes($target, $damaged)
    try {
        $result = & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $destination 'Verify-Package.ps1') 2>&1
        if ($LASTEXITCODE -eq 0) { throw 'Package verifier accepted a damaged assembly' }
    }
    finally { [IO.File]::WriteAllBytes($target, $original) }
    Write-Output "DESKTOP_PACKAGE_OK $product exe_at_root complete_self_contained ps51_verify no_user_state corruption_rejected extracted=$destination"
}
