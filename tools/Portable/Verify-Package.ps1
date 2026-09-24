#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') + '\'
$manifest = Get-Content -LiteralPath (Join-Path $root 'package-manifest.json') -Raw | ConvertFrom-Json
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
Write-Output ("Package verified: {0} files, build {1}." -f $manifest.files.Count, $manifest.created_utc)
