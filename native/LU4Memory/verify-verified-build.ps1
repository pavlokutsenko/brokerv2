$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$artifactRoot = Join-Path $root 'artifacts\verified'
$manifest = Get-Content -LiteralPath (Join-Path $artifactRoot 'manifest.json') -Raw | ConvertFrom-Json
$driver = Join-Path $artifactRoot $manifest.artifact

if (-not (Test-Path -LiteralPath $driver)) {
    throw "Verified driver is missing: $driver"
}

$file = Get-Item -LiteralPath $driver
$hash = (Get-FileHash -LiteralPath $driver -Algorithm SHA256).Hash
if ($file.Length -ne $manifest.length) {
    throw "Length mismatch: expected $($manifest.length), got $($file.Length)"
}
if ($hash -ne $manifest.sha256) {
    throw "SHA-256 mismatch: expected $($manifest.sha256), got $hash"
}

[pscustomobject]@{
    Path = $file.FullName
    Length = $file.Length
    SHA256 = $hash
    Status = 'verified'
} | ConvertTo-Json
