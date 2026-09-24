$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\amd64\MSBuild.exe'
foreach ($project in @('ClientLaunch.Agent.vcxproj', 'ClientLaunch.Version.vcxproj', 'ClientLaunch.Login.vcxproj')) {
    & $msbuild (Join-Path $root $project) '/t:Rebuild' '/p:Configuration=Release' '/p:Platform=x64' '/m' '/v:minimal'
    if ($LASTEXITCODE -ne 0) { throw "Native client launch build failed: $project" }
}
foreach ($name in @('PriceCheck.ClientAgent.dll', 'version.dll', 'PriceCheck.ClientLogin.dll')) {
    $path = Join-Path $root "build\x64\$name"
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing native artifact: $path" }
    Get-Item -LiteralPath $path | Select-Object FullName,Length
}
