#requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$Source,
      [Parameter(Mandatory=$true)][ValidateSet('Launcher','Collector')][string]$Product,
      [Parameter(Mandatory=$true)][string]$Dotnet)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourceRoot = (Resolve-Path -LiteralPath $Source).ProviderPath.TrimEnd('\')
$prefix = $sourceRoot + '\'
$entryPoint = "PriceCheck.$Product.exe"
$payload = Join-Path $sourceRoot 'runtime'
if (Test-Path -LiteralPath $payload) { throw 'Portable layout needs a fresh flat publication.' }
if ((Get-Item -LiteralPath $sourceRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Publication root is a reparse point.' }
$items = @(Get-ChildItem -LiteralPath $sourceRoot -Force | Where-Object { $_.Name -ne $entryPoint })
foreach ($item in $items) {
    $resolved = (Resolve-Path -LiteralPath $item.FullName).ProviderPath
    if (-not $resolved.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetDirectoryName($resolved) -ne $sourceRoot -or
        ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Invalid publication item.' }
    if ($item.PSIsContainer -and @(Get-ChildItem -LiteralPath $resolved -Recurse -Force |
        Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Publication contains a reparse point.' }
}
& $Dotnet msbuild (Join-Path $repo "src\PriceCheck.$Product\PriceCheck.$Product.csproj") -restore `
    -target:CreatePortableAppHost -property:Configuration=Release -property:RuntimeIdentifier=win-x64 `
    -property:SelfContained=true "-property:CustomAfterMicrosoftCommonTargets=$PSScriptRoot\PortableAppHost.targets" `
    "-property:PortableAppHostDestination=$sourceRoot\$entryPoint" -nologo -verbosity:minimal
if ($LASTEXITCODE -ne 0) { throw 'Portable apphost generation failed.' }
[void][IO.Directory]::CreateDirectory($payload)
foreach ($item in $items) {
    $target = [IO.Path]::GetFullPath((Join-Path $payload $item.Name))
    if ([IO.Path]::GetDirectoryName($target) -ne $payload) { throw 'Invalid runtime destination.' }
    Move-Item -LiteralPath $item.FullName -Destination $target
}
$infoPath = Join-Path $payload 'build-info.json'
$info = Get-Content -LiteralPath $infoPath -Raw | ConvertFrom-Json
$info | Add-Member -NotePropertyName Layout -NotePropertyValue 'exe-with-runtime' -Force
$info | Add-Member -NotePropertyName EntryPoint -NotePropertyValue $entryPoint -Force
$info | Add-Member -NotePropertyName ExeSha256 -NotePropertyValue (Get-FileHash -LiteralPath (Join-Path $sourceRoot $entryPoint)).Hash -Force
$info | ConvertTo-Json | Set-Content -LiteralPath $infoPath -Encoding utf8
Write-Host "Portable layout: $entryPoint + runtime\"
