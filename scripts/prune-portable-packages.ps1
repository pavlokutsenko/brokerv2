#requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$KeepArchive,
      [Parameter(Mandatory=$true)][ValidateSet('Launcher','Collector')][string]$Product)
$ErrorActionPreference = 'Stop'
$archive = (Resolve-Path -LiteralPath $KeepArchive).Path
$directory = Split-Path -Parent $archive
$pattern = '^PriceCheck' + $Product + '-win-x64-\d{8}-\d{6}\.zip(?:\.sha256)?$'
if ([IO.Path]::GetFileName($archive) -notmatch $pattern -or $archive -notlike '*.zip') { throw 'Unexpected archive name for cleanup.' }
if ((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Package directory must not be a link.' }
$expected = (Get-Content -LiteralPath ($archive + '.sha256') -Raw).Split(' ')[0]
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'Keep archive checksum differs; old archives retained.' }
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $entry = "PriceCheck.$Product.exe"
    if (@($zip.Entries | Where-Object { $_.FullName -eq $entry }).Count -ne 1 -or
        @($zip.Entries | Where-Object { $_.FullName -ne $entry -and $_.FullName -notlike 'runtime/*' }).Count -ne 0 -or
        @($zip.Entries | Where-Object { $_.FullName -eq 'runtime/package-manifest.json' }).Count -ne 1) {
        throw 'Keep archive structure differs; old archives retained.'
    }
} finally { $zip.Dispose() }
$keep = @($archive, ($archive + '.sha256'))
$older = @(Get-ChildItem -LiteralPath $directory -File -Force | Where-Object {
    $_.Name -match $pattern -and $_.FullName -notin $keep -and $_.LastWriteTimeUtc -le (Get-Item -LiteralPath $archive).LastWriteTimeUtc
})
foreach ($file in $older) {
    $target = [IO.Path]::GetFullPath($file.FullName)
    if ([IO.Path]::GetDirectoryName($target) -ne $directory -or $file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Unsafe cleanup path.' }
    Remove-Item -LiteralPath $target -Force
}
Write-Host "$Product packages: removed $($older.Count) old archive/checksum files."
