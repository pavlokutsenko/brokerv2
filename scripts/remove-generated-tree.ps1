param([Parameter(Mandatory=$true)][string]$Path,[Parameter(Mandatory=$true)][string]$Root)
$ErrorActionPreference='Stop'
if(-not (Test-Path -LiteralPath $Path)){return}
$base=(Resolve-Path -LiteralPath $Root).Path.TrimEnd('\')+'\'
$target=Get-Item -LiteralPath $Path -Force
if(-not $target.FullName.StartsWith($base,[StringComparison]::OrdinalIgnoreCase) -or
    ($target.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw "Unsafe generated target: $Path"}
$entries=@(Get-ChildItem -LiteralPath $target.FullName -Recurse -Force)
if($entries | Where-Object {($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
    -not $_.FullName.StartsWith($target.FullName+'\',[StringComparison]::OrdinalIgnoreCase)}){throw "Unsafe generated entry: $Path"}
foreach($entry in $entries | Where-Object {-not $_.PSIsContainer}){Remove-Item -LiteralPath $entry.FullName}
foreach($entry in $entries | Where-Object {$_.PSIsContainer} | Sort-Object {$_.FullName.Length} -Descending){Remove-Item -LiteralPath $entry.FullName}
Remove-Item -LiteralPath $target.FullName
