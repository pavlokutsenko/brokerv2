#requires -Version 7.0
[CmdletBinding()]
param([string]$PublishDirectory)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$work = Join-Path $repo ('workspace\portable-package-' + $stamp + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
if (-not $PublishDirectory) {
    $PublishDirectory = Join-Path $work 'publish'
    & (Join-Path $repo 'build.ps1') -OutputDirectory $PublishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publication failed' }
}
$publish = [IO.Path]::GetFullPath($PublishDirectory).TrimEnd('\') + '\'
$package = Join-Path $work 'PriceCheckCollector'
[void][IO.Directory]::CreateDirectory($package)
$inputs = @(Get-ChildItem -LiteralPath $publish -Recurse -File)
foreach ($sourceFile in $inputs) {
    $relative = $sourceFile.FullName.Substring($publish.Length)
    if ($relative -match '(^|\\)(__pycache__|build)(\\|$)' -or $sourceFile.Extension -in @('.pdb', '.pyc')) { continue }
    if ($relative -match '(^|\\)(data|runtime-sessions|logs|research|manual-captures|broker-snapshots|price-snapshots)(\\|$)' -or
        $sourceFile.Name -in @('profiles.json', 'launch-templates.json') -or
        $sourceFile.Extension -in @('.log','.csv','.dmp','.bin','.db','.sqlite') -or
        ($sourceFile.Extension -eq '.json' -and $sourceFile.Name -notin @('PriceCheck.Collector.deps.json','PriceCheck.Collector.runtimeconfig.json'))) {
        throw "Publish directory contains non-distributable state: $relative. Use a fresh build output."
    }
    $target = Join-Path $package $relative
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $target))
    Copy-Item -LiteralPath $sourceFile.FullName -Destination $target
}
foreach ($required in @(
    'PriceCheck.Collector.exe','PriceCheck.Collector.dll','PriceCheck.Collector.deps.json',
    'PriceCheck.Launching.dll','PriceCheck.Collection.dll','PriceCheck.Contracts.dll','PriceCheck.Windows.dll',
    'PriceCheck.Collector.runtimeconfig.json','hostfxr.dll','hostpolicy.dll','coreclr.dll','clrjit.dll',
    'PresentationFramework.dll','ClientLaunchRuntime\PriceCheck.ClientAgent.dll',
    'ClientLaunchRuntime\PriceCheck.ClientLogin.dll','BrokerRuntime\BrokerWorker.exe',
    'BrokerRuntime\_internal\python314.dll','BrokerRuntime\_internal\base_library.zip',
    'DriverRuntime\lu4_memory_wfp.sys','DriverRuntime\load-driver.ps1',
    'DriverRuntime\kdu.exe','DriverRuntime\drv64.dll','DriverRuntime\Taigei64.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $package $required) -PathType Leaf)) { throw "Missing runtime: $required" }
}
Copy-Item -Path (Join-Path $repo 'tools\Portable\*') -Destination $package
$files = @(Get-ChildItem -LiteralPath $package -Recurse -File | Sort-Object FullName | ForEach-Object {
    @{ path = $_.FullName.Substring($package.Length + 1).Replace('\','/'); bytes = $_.Length;
       sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$manifest = @{ schema = 1; created_utc = [datetimeoffset]::UtcNow.ToString('o'); target = 'win-x64';
    self_contained = $true; includes_user_settings = $false; files = $files }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $package 'package-manifest.json') -Encoding utf8
& (Join-Path $package 'Verify-Package.ps1')
$destination = Join-Path $repo 'release\packages'
[void][IO.Directory]::CreateDirectory($destination)
$archive = Join-Path $destination ("PriceCheckCollector-win-x64-$stamp.zip")
[IO.Compression.ZipFile]::CreateFromDirectory($package, $archive, [IO.Compression.CompressionLevel]::Optimal, $true)
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
[IO.File]::WriteAllText($archive + '.sha256', ($hash + '  ' + [IO.Path]::GetFileName($archive) + "`n"))
@{ archive = $archive; bytes = (Get-Item -LiteralPath $archive).Length; sha256 = $hash;
   file_count = $files.Count; staging = $package } | ConvertTo-Json
