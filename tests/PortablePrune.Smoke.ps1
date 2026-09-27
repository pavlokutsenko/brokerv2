#requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$Archive)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$work = Join-Path $repo ('workspace\portable-prune-smoke-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($work)
$product = if ([IO.Path]::GetFileName($Archive) -like 'PriceCheckLauncher-*') { 'Launcher' } else { 'Collector' }
$keep = Join-Path $work ([IO.Path]::GetFileName($Archive))
Copy-Item -LiteralPath $Archive -Destination $keep
Copy-Item -LiteralPath ($Archive + '.sha256') -Destination ($keep + '.sha256')
$older = Join-Path $work "PriceCheck$product-win-x64-20200101-000000.zip"
[IO.File]::WriteAllText($older, 'old archive fixture')
[IO.File]::WriteAllText($older + '.sha256', 'old checksum fixture')
foreach ($path in @($older, ($older + '.sha256'))) { (Get-Item -LiteralPath $path).LastWriteTimeUtc = (Get-Item -LiteralPath $keep).LastWriteTimeUtc.AddMinutes(-1) }
$unrelated = Join-Path $work 'unrelated.zip'; [IO.File]::WriteAllText($unrelated, 'keep')
$extracted = Join-Path $work "PriceCheck$product-win-x64-20200101-000000"
[void][IO.Directory]::CreateDirectory($extracted)
$checksum = [IO.File]::ReadAllText($keep + '.sha256')
[IO.File]::WriteAllText($keep + '.sha256', 'invalid checksum')
try { & (Join-Path $repo 'scripts\prune-portable-packages.ps1') -KeepArchive $keep -Product $product; throw 'Invalid checksum accepted.' }
catch { if ($_.Exception.Message -notlike '*checksum differs*') { throw } }
if (-not (Test-Path -LiteralPath $older)) { throw 'Old archive deleted before verification.' }
[IO.File]::WriteAllText($keep + '.sha256', $checksum)
& (Join-Path $repo 'scripts\prune-portable-packages.ps1') -KeepArchive $keep -Product $product
if ((Test-Path -LiteralPath $older) -or (Test-Path -LiteralPath ($older + '.sha256')) -or
    -not (Test-Path -LiteralPath $keep) -or -not (Test-Path -LiteralPath $unrelated) -or -not (Test-Path -LiteralPath $extracted)) {
    throw 'Cleanup retained an old archive or removed an unrelated path.'
}
Write-Host 'PORTABLE_PRUNE_OK failed verification preserves old archives; successful verification removes old pairs only.'
