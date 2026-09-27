param(
    [string]$Source,
    [Parameter(Mandatory=$true)][string]$Destination,
    [ValidateSet('Launcher','Collector')][string]$Product = 'Collector',
    [switch]$Restore
)
$ErrorActionPreference = 'Stop'
$Destination = [IO.Path]::GetFullPath($Destination)
$statePath = Join-Path $Destination '.package-state.json'
$live = @(Get-Process -Name "PriceCheck.$Product" -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq (Join-Path $Destination "PriceCheck.$Product.exe") })
if ($live.Count) { throw "Stop the installed $Product before publication/recovery." }

function Get-PackageHash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $sha.Dispose(); $stream.Dispose() }
}

function Write-DurableFile([string]$From, [string]$To, [string]$Backup = $null) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($To)) | Out-Null
    $temporary = $To + '.pending'
    $inputStream = [IO.File]::OpenRead($From)
    $outputStream = [IO.FileStream]::new($temporary, [IO.FileMode]::Create,
        [IO.FileAccess]::Write, [IO.FileShare]::None, 65536, [IO.FileOptions]::WriteThrough)
    try { $inputStream.CopyTo($outputStream); $outputStream.Flush($true) }
    finally { $outputStream.Dispose(); $inputStream.Dispose() }
    if ((Get-PackageHash $From) -ne (Get-PackageHash $temporary)) { throw "Copy verification failed: $To" }
    if ([IO.File]::Exists($To)) {
        if ($Backup) { [IO.File]::Replace($temporary, $To, $Backup) }
        else { [IO.File]::Replace($temporary, $To, [NullString]::Value) }
    }
    else { [IO.File]::Move($temporary, $To) }
}

if (-not $Restore) {
    if (-not $Source) { throw 'Source is required for publication.' }
    $Source = [IO.Path]::GetFullPath($Source)
    if ($Source -eq $Destination) { throw 'Publish from a separate staging directory.' }
    # Validate the launcher before committing a package. A zero-filled config
    # has the expected length but cannot be used by the .NET host.
    $runtime = Get-Content -LiteralPath (Join-Path $Source "PriceCheck.$Product.runtimeconfig.json") -Raw | ConvertFrom-Json
    if (-not $runtime.runtimeOptions.tfm) { throw "$Product runtimeconfig has no valid runtimeOptions/tfm." }
    $versionPath = Join-Path ($Destination + '.package-recovery') ([Guid]::NewGuid().ToString('N'))
    $entries = @()
    foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File) {
        $relative = $file.FullName.Substring($Source.Length + 1)
        if ($relative -like '.package-*' -or $relative -like '*.pending') { continue }
        if ($file.Length -eq 0 -and $file.Extension -in '.exe','.dll','.sys','.json') {
            throw "Empty required package file: $relative"
        }
        if ($file.Extension -in '.exe','.dll','.sys','.json','.py','.ps1' -and $file.Length -gt 0) {
            $stream = [IO.File]::OpenRead($file.FullName)
            try {
                $header = New-Object byte[] 64
                $count = $stream.Read($header,0,$header.Length)
                if ($count -gt 0 -and -not ($header | Where-Object { $_ -ne 0 })) {
                    throw "Zero-filled package file: $relative"
                }
            } finally { $stream.Dispose() }
        }
        $copy = Join-Path $versionPath $relative
        Write-DurableFile $file.FullName $copy
        $entries += [pscustomobject]@{ Path=$relative; Length=$file.Length; Sha256=(Get-PackageHash $copy) }
    }
    $state = [pscustomobject]@{ Version=1; VersionPath=$versionPath; Files=$entries }
    $manifestSource = Join-Path $versionPath 'manifest.json'
    [IO.File]::WriteAllText($manifestSource, ($state | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    # Write the commit record through the same flushed atomic path BEFORE
    # replacing installed files. Restore can finish an interrupted publication.
    Write-DurableFile $manifestSource $statePath ($statePath + '.bak')
} else {
    $state = $null
    foreach ($candidate in @($statePath, $statePath+'.bak')) {
        if (-not (Test-Path -LiteralPath $candidate)) { continue }
        try { $state = Get-Content -LiteralPath $candidate -Raw | ConvertFrom-Json; break }
        catch { }
    }
    if (-not $state) { throw 'No valid committed package manifest; rebuild with build.ps1.' }
}

$recoveryRoot = [IO.Path]::GetFullPath($Destination + '.package-recovery') + [IO.Path]::DirectorySeparatorChar
$version = [IO.Path]::GetFullPath($state.VersionPath)
if ($state.Version -ne 1 -or -not $version.StartsWith($recoveryRoot,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Invalid package recovery path.'
}
$changed = 0
foreach ($entry in $state.Files) {
    $target = [IO.Path]::GetFullPath((Join-Path $Destination $entry.Path))
    if (-not $target.StartsWith($Destination + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Package entry escapes destination.'
    }
    if ((Test-Path -LiteralPath $target) -and (Get-PackageHash $target) -eq $entry.Sha256) { continue }
    $copy = Join-Path $version $entry.Path
    if ((Get-PackageHash $copy) -ne $entry.Sha256) { throw "Recovery copy damaged: $($entry.Path)" }
    Write-DurableFile $copy $target
    $changed++
}
Write-Host "Durable package: $($state.Files.Count) verified files, $changed installed/restored."
