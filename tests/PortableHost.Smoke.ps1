#requires -Version 7.0
param([Parameter(Mandatory=$true)][string[]]$Directories)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dotnet = 'C:\tools\dev\.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }
& $dotnet build (Join-Path $PSScriptRoot 'PortableHost.Smoke\PortableHost.Smoke.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Portable host probe build failed.' }
$hook = Join-Path $PSScriptRoot 'PortableHost.Smoke\bin\Release\net8.0-windows\PortableHost.Smoke.dll'
$work = Join-Path $repo ('workspace\portable-host-smoke-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($work)
$production = @{}
foreach ($product in @('Launcher','Collector')) {
    $settings = Join-Path $env:LOCALAPPDATA "PriceCheck$product"
    foreach ($name in @('profiles.json','launch-templates.json','profiles.json.bak','launch-templates.json.bak')) {
        $path = Join-Path $settings $name
        $production[$path] = if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path).Hash } else { $null }
    }
}
foreach ($directory in $Directories) {
    $directory = [IO.Path]::GetFullPath($directory)
    $info = Get-Content -LiteralPath (Join-Path $directory 'runtime\build-info.json') -Raw | ConvertFrom-Json
    $output = Join-Path $work ($info.Product + '.json')
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $directory $info.EntryPoint))
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.WorkingDirectory = $env:WINDIR
    $start.Environment['DOTNET_STARTUP_HOOKS'] = $hook
    $start.Environment['PRICECHECK_PACKAGE_PROBE_OUTPUT'] = $output
    $start.Environment['DOTNET_ROOT'] = Join-Path $work 'absent-dotnet'
    $start.Environment['DOTNET_ROOT_X64'] = Join-Path $work 'absent-dotnet'
    $start.Environment['COREHOST_TRACE'] = '1'
    $start.Environment['COREHOST_TRACEFILE'] = Join-Path $work ($info.Product + '-host.txt')
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Portable EXE probe timed out.' }
        if (-not (Test-Path -LiteralPath $output)) { throw 'Portable EXE did not reach the managed entry assembly.' }
        $result = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
        if ($process.ExitCode -ne 0 -or -not $result.WpfLoaded) { throw "Portable EXE failed: $($result.Error)" }
    } finally { $process.Dispose() }
    Write-Output "PORTABLE_HOST_OK $($info.Product) root_exe runtime_base wpf_loaded independent_cwd no_global_dotnet"
}
foreach ($path in $production.Keys) {
    $current = if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path).Hash } else { $null }
    if ($current -ne $production[$path]) { throw 'Portable host probe changed production settings.' }
}
Write-Output "PORTABLE_HOST_SETTINGS_UNCHANGED output=$work"
