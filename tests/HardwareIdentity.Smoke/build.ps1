param([string]$Dotnet = 'C:\tools\dev\.tools\dotnet\dotnet.exe')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$taskMsbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\amd64\MSBuild.exe'
if (-not (Test-Path -LiteralPath $taskMsbuild)) { throw "MSBuild not found: $taskMsbuild" }
if (-not (Test-Path -LiteralPath $Dotnet)) { $Dotnet = 'dotnet' }
& $taskMsbuild (Join-Path $taskRoot 'IdentityNativeProbe.vcxproj') '/p:Configuration=Release' '/p:Platform=x64' '/p:NuGetProjectStyle=None' '/m' '/nologo' '/verbosity:minimal'
if ($LASTEXITCODE -ne 0) { throw "Native identity probe build failed: $LASTEXITCODE" }
& $Dotnet run --project (Join-Path $taskRoot 'HardwareIdentity.Smoke.csproj') -c Release -- (Join-Path $taskRoot 'build\IdentityNativeProbe.exe')
if ($LASTEXITCODE -ne 0) { throw "Hardware identity checks failed: $LASTEXITCODE" }
