#requires -Version 7.0
[CmdletBinding()]
param([string]$PublishDirectory,[ValidateSet('Launcher','Collector')][string]$Product='Collector',
      [string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$work = Join-Path $repo ('workspace\portable-package-' + $stamp + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
if (-not $PublishDirectory) {
    $collectorPublish = Join-Path $work 'collector-publish'
    $launcherPublish = Join-Path $work 'launcher-publish'
    & (Join-Path $repo 'build.ps1') -OutputDirectory $collectorPublish -LauncherOutputDirectory $launcherPublish -SkipPackages
    if ($LASTEXITCODE -ne 0) { throw 'Publication failed' }
    $PublishDirectory = if ($Product -eq 'Launcher') { $launcherPublish } else { $collectorPublish }
}
$publish = [IO.Path]::GetFullPath($PublishDirectory).TrimEnd('\') + '\'
$package = Join-Path $work "PriceCheck$Product"
[void][IO.Directory]::CreateDirectory($package)
$inputs = @(Get-ChildItem -LiteralPath $publish -Recurse -File)
foreach ($sourceFile in $inputs) {
    $relative = $sourceFile.FullName.Substring($publish.Length)
    if ($relative -like '.package-*' -or $relative -like '*.pending') { continue }
    if ($relative -match '(^|\\)(__pycache__|build)(\\|$)' -or $sourceFile.Extension -in @('.pdb', '.pyc')) { continue }
    if ($relative -match '(^|\\)(data|runtime-sessions|logs|research|manual-captures|broker-snapshots|price-snapshots)(\\|$)' -or
        $sourceFile.Name -in @('profiles.json', 'launch-templates.json') -or
        $sourceFile.Extension -in @('.log','.csv','.dmp','.bin','.db','.sqlite') -or
        ($sourceFile.Extension -eq '.json' -and $sourceFile.Name -notin @("PriceCheck.$Product.deps.json","PriceCheck.$Product.runtimeconfig.json",'build-info.json') -and
         $relative -notmatch '^runtime\\BrokerRuntime\\_internal\\navigation\\maps\\')) {
        throw "Publish directory contains non-distributable state: $relative. Use a fresh build output."
    }
    $target = Join-Path $package $relative
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $target))
    Copy-Item -LiteralPath $sourceFile.FullName -Destination $target
}
$payload = Join-Path $package 'runtime'
$requiredFiles = @(
    "PriceCheck.$Product.dll","PriceCheck.$Product.deps.json",
    'PriceCheck.Launching.UI.dll','PriceCheck.Launching.dll','PriceCheck.Contracts.dll','PriceCheck.Windows.dll',
    "PriceCheck.$Product.runtimeconfig.json",'hostfxr.dll','hostpolicy.dll','coreclr.dll','clrjit.dll',
    'PresentationFramework.dll','ClientLaunchRuntime\PriceCheck.ClientAgent.dll',
    'ClientLaunchRuntime\PriceCheck.ClientLogin.dll',
    'DriverRuntime\lu4_memory_wfp.sys','DriverRuntime\load-driver.ps1',
    'DriverRuntime\kdu.exe','DriverRuntime\drv64.dll','DriverRuntime\Taigei64.dll')
if ($Product -eq 'Collector') {
    $requiredFiles += @('PriceCheck.Collection.dll','BrokerRuntime\BrokerWorker.exe','BrokerRuntime\_internal\python314.dll','BrokerRuntime\_internal\base_library.zip')
} elseif ((Test-Path -LiteralPath (Join-Path $payload 'PriceCheck.Collection.dll')) -or (Test-Path -LiteralPath (Join-Path $payload 'BrokerRuntime'))) {
    throw 'Launcher publication contains collection components.'
}
foreach ($required in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $required) -PathType Leaf)) { throw "Missing runtime: $required" }
}
$rootFiles = @(Get-ChildItem -LiteralPath $package -File -Force)
$rootDirectories = @(Get-ChildItem -LiteralPath $package -Directory -Force)
if ($rootFiles.Count -ne 1 -or $rootFiles[0].Name -ne "PriceCheck.$Product.exe" -or
    $rootDirectories.Count -ne 1 -or $rootDirectories[0].Name -ne 'runtime') { throw 'Package root must contain only the executable and runtime directory.' }
Copy-Item -LiteralPath (Join-Path $repo 'tools\Portable\Verify-Package.ps1') -Destination $payload
Copy-Item -LiteralPath (Join-Path $repo "tools\Portable\$Product-README.txt") -Destination (Join-Path $payload 'README-FIRST.txt')
if ($Product -eq 'Collector') {
    foreach ($name in @('Collect-Diagnostics.ps1','Start-WithDiagnostics.ps1')) {
        Copy-Item -LiteralPath (Join-Path $repo "tools\Portable\$name") -Destination $payload
    }
}
$files = @(Get-ChildItem -LiteralPath $package -Recurse -File | Sort-Object FullName | ForEach-Object {
    @{ path = $_.FullName.Substring($package.Length + 1).Replace('\','/'); bytes = $_.Length;
       sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$manifest = @{ schema = 3; product = $Product; entry_point = "PriceCheck.$Product.exe"; runtime_directory = 'runtime'; created_utc = [datetimeoffset]::UtcNow.ToString('o'); target = 'win-x64';
    self_contained = $true; includes_user_settings = $false; includes_server = $false; files = $files }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payload 'package-manifest.json') -Encoding utf8
$manifestStream = [IO.File]::Open((Join-Path $payload 'package-manifest.json'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::Read)
try { $manifestStream.Flush($true) } finally { $manifestStream.Dispose() }
& (Join-Path $payload 'Verify-Package.ps1')
$destination = if ($PackageDirectory) { [IO.Path]::GetFullPath($PackageDirectory, $repo) } else { Join-Path $repo 'release\packages' }
[void][IO.Directory]::CreateDirectory($destination)
$archive = Join-Path $destination ("PriceCheck$Product-win-x64-$stamp.zip")
$pendingArchive = $archive + '.pending'
[IO.Compression.ZipFile]::CreateFromDirectory($package, $pendingArchive, [IO.Compression.CompressionLevel]::Optimal, $false)
$archiveStream = [IO.File]::Open($pendingArchive, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::Read)
try { $archiveStream.Flush($true) } finally { $archiveStream.Dispose() }
[IO.File]::Move($pendingArchive, $archive)
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
[IO.File]::WriteAllText($archive + '.sha256', ($hash + '  ' + [IO.Path]::GetFileName($archive) + "`n"))
$hashStream = [IO.File]::Open($archive + '.sha256', [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::Read)
try { $hashStream.Flush($true) } finally { $hashStream.Dispose() }
& (Join-Path $repo 'scripts\prune-portable-packages.ps1') -KeepArchive $archive -Product $Product
@{ product = $Product; archive = $archive; entry_point = "PriceCheck.$Product.exe"; bytes = (Get-Item -LiteralPath $archive).Length; sha256 = $hash;
   file_count = $files.Count; staging = $package } | ConvertTo-Json
