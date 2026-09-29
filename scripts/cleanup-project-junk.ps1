Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$workspace = [IO.Path]::GetFullPath((Join-Path $repo 'workspace')).TrimEnd('\')
$expected = [IO.Path]::GetFullPath('C:\broker\workspace').TrimEnd('\')
$comparison = [StringComparison]::OrdinalIgnoreCase

if (-not $workspace.Equals($expected, $comparison) -or
    -not (Test-Path -LiteralPath (Join-Path $repo 'AGENTS.md') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $repo '.git') -PathType Container)) {
    throw 'This script may run only from the C:\broker repository.'
}

$workspaceItem = Get-Item -LiteralPath $workspace -Force
if (-not $workspaceItem.PSIsContainer -or
    ($workspaceItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'The workspace directory is missing or is a link.'
}

$tracked = @(& git -C $repo ls-files -- workspace)
if ($LASTEXITCODE -ne 0 -or $tracked.Count -ne 0) {
    throw 'Tracked files exist in workspace, or Git could not verify it. Nothing was deleted.'
}

$active = @(Get-CimInstance Win32_Process | Where-Object {
    $_.ExecutablePath -and $_.ExecutablePath.StartsWith(($workspace + '\'), $comparison)
})
if ($active.Count -ne 0) {
    throw 'A program is running from workspace. Close it before cleanup.'
}

function Assert-WorkspaceChild([string]$path) {
    $full = [IO.Path]::GetFullPath($path)
    if (-not $full.StartsWith(($workspace + '\'), $comparison)) {
        throw "Refusing to touch a path outside workspace: $full"
    }
}

$sourceExtensions = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
@(
    '.c', '.cc', '.cpp', '.cxx', '.h', '.hpp', '.hxx', '.asm', '.s',
    '.cs', '.csproj', '.sln', '.slnx', '.fs', '.fsproj', '.vb', '.vbproj',
    '.py', '.ps1', '.psm1', '.psd1', '.java', '.kt', '.kts', '.lua', '.sql',
    '.js', '.jsx', '.ts', '.tsx', '.sh', '.bat', '.cmd',
    '.xaml', '.html', '.css', '.scss', '.resx', '.rc', '.def', '.idl',
    '.wxs', '.wxi', '.iss', '.props', '.targets', '.nuspec', '.manifest',
    '.md', '.rst', '.json', '.yaml', '.yml', '.toml', '.ini', '.cfg',
    '.config', '.xml', '.editorconfig', '.gitignore', '.gitattributes'
) | ForEach-Object { [void]$sourceExtensions.Add($_) }

$protectedRoots = @('ghidra', 'GhidraScripts')
$generatedParts = @('python-libs', 'site-packages', '__pycache__', 'obj', 'bin',
    '.venv', 'venv', 'BrokerRuntime', '_internal')
$generatedRoots = @(Get-ChildItem -LiteralPath $workspace -Directory -Force |
    Where-Object {
        $_.Name.EndsWith('.package-recovery', $comparison) -or
        $_.Name.StartsWith('verify-commit-', $comparison) -or
        (Test-Path -LiteralPath (Join-Path $_.FullName 'build-info.json')) -or
        (Test-Path -LiteralPath (Join-Path $_.FullName 'PriceCheck.Collector.exe')) -or
        (Test-Path -LiteralPath (Join-Path $_.FullName 'PriceCheck.Launcher.exe'))
    } | ForEach-Object Name)
$entries = @(Get-ChildItem -LiteralPath $workspace -Force -Recurse)
$links = @($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
foreach ($link in $links) {
    Assert-WorkspaceChild $link.FullName
    if (-not $link.FullName.Substring($workspace.Length + 1).Equals($link.Name, $comparison) -or
        -not $link.PSIsContainer -or $link.LinkType -ne 'Junction') {
        throw "Unexpected link inside workspace: $($link.FullName). Nothing was deleted."
    }
}

$embeddedGit = @($entries | Where-Object { $_.PSIsContainer -and $_.Name -eq '.git' })
if ($embeddedGit.Count -ne 0) {
    throw 'An embedded Git repository exists in workspace. Nothing was deleted.'
}

$deleteFiles = [Collections.Generic.List[IO.FileInfo]]::new()
foreach ($file in @($entries | Where-Object { -not $_.PSIsContainer })) {
    Assert-WorkspaceChild $file.FullName
    $parts = $file.FullName.Substring($workspace.Length + 1).Split('\')
    if ($protectedRoots -contains $parts[0]) { continue }

    $generated = ($generatedRoots -contains $parts[0]) -or
        (@($parts | Where-Object { $generatedParts -contains $_ }).Count -ne 0)
    $source = $sourceExtensions.Contains($file.Extension) -or
        @('Makefile', 'Dockerfile', 'LICENSE', 'README', '.editorconfig',
          '.gitignore', '.gitattributes', '.clang-format') -contains $file.Name
    if ($parts.Count -eq 1 -and $file.Extension -in @('.json', '.xml')) {
        $source = $false
    }
    if ($generated -or -not $source) {
        $deleteFiles.Add($file)
    }
}

$emptyDirectoryCandidates = @($entries | Where-Object {
    $_.PSIsContainer -and -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint)
} | Sort-Object { $_.FullName.Length } -Descending)
Write-Host "Removing $($deleteFiles.Count) generated files and $($links.Count) junctions from $workspace"

foreach ($file in $deleteFiles) {
    Remove-Item -LiteralPath $file.FullName -Force
}

# Non-recursive deletion removes the junction itself, never its game-directory target.
foreach ($link in $links) {
    [IO.Directory]::Delete($link.FullName, $false)
}

foreach ($directory in $emptyDirectoryCandidates) {
    Assert-WorkspaceChild $directory.FullName
    $relative = $directory.FullName.Substring($workspace.Length + 1)
    if ($protectedRoots -contains $relative.Split('\')[0]) { continue }
    if (-not (Get-ChildItem -LiteralPath $directory.FullName -Force | Select-Object -First 1)) {
        Remove-Item -LiteralPath $directory.FullName -Force
    }
}

Write-Host 'Done. Ghidra, source files, repository source, release, and LocalAppData remain untouched.'
