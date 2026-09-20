$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'LU4Memory.vcxproj'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\amd64\MSBuild.exe'

if (-not (Test-Path -LiteralPath $msbuild)) {
    throw "MSBuild not found: $msbuild"
}

& $msbuild $project '/t:Rebuild' '/p:Configuration=Release' '/p:Platform=x64' '/p:SpectreMitigation=false' '/m'
if ($LASTEXITCODE -ne 0) {
    throw "Driver build failed with exit code $LASTEXITCODE"
}

$driver = Join-Path $root 'build\x64\Release\lu4_memory.sys'
if (-not (Test-Path -LiteralPath $driver)) {
    throw "Build completed without expected driver: $driver"
}

$file = Get-Item -LiteralPath $driver
$hash = Get-FileHash -LiteralPath $driver -Algorithm SHA256
[pscustomobject]@{
    Path = $file.FullName
    Length = $file.Length
    SHA256 = $hash.Hash
} | ConvertTo-Json
