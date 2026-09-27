$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\amd64\MSBuild.exe'
& $msbuild (Join-Path $root 'NativeGuardProbe.vcxproj') /t:Rebuild /p:Configuration=Release /p:Platform=x64 /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Native guard probe did not build.' }
$probe = Join-Path $root 'build\x64\NativeGuardProbe.exe'
& $probe
if ($LASTEXITCODE -ne 0) { throw 'Native guards failed.' }
& $probe '--prelogin-pending-envelope'
if ($LASTEXITCODE -ne 0) { throw 'Pending world envelope was rejected before login.' }
foreach ($mode in @('--watchdog-hwid', '--watchdog-world', '--watchdog-envelope', '--watchdog-controller')) {
    & $probe $mode
    if ($LASTEXITCODE -ne 0x50430004) { throw "Watchdog failed for $mode; exit $LASTEXITCODE" }
    Write-Host "NATIVE_WATCHDOG_OK $mode stopped the synthetic process"
}
$global:LASTEXITCODE = 0
