param([int]$OlderThanHours=2)
$ErrorActionPreference='Stop'
if($OlderThanHours -lt 1){throw 'Keep at least one hour of recent build work.'}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspace=(Resolve-Path -LiteralPath (Join-Path $repo 'workspace')).Path
$cutoff=(Get-Date).AddHours(-$OlderThanHours)
$targets=@(Get-ChildItem -LiteralPath $workspace -Directory | Where-Object {
    $_.CreationTime -lt $cutoff -and $_.Name -match '^(publish-(?:(?:Collector|Launcher)-)?[a-f0-9]{32}|portable-package-\d{8}-\d{6}-[a-f0-9]{6})$'
})
$live=@(Get-CimInstance Win32_Process | Where-Object {$_.ExecutablePath} | Select-Object -ExpandProperty ExecutablePath)
$freed=0L;$removed=0
foreach($target in $targets){
    $root=(Resolve-Path -LiteralPath $target.FullName).Path
    if(-not $root.StartsWith($workspace+'\',[StringComparison]::OrdinalIgnoreCase) -or
        ($target.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw "Unexpected temporary path: $root"}
    if($live | Where-Object {$_.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)}){continue}
    $entries=@(Get-ChildItem -LiteralPath $root -Recurse)
    if($entries | Where-Object {($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        -not $_.FullName.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)}){throw "Unsafe temporary entry: $root"}
    foreach($entry in $entries | Where-Object {-not $_.PSIsContainer}){
        $freed+=$entry.Length;Remove-Item -LiteralPath $entry.FullName
    }
    foreach($entry in $entries | Where-Object {$_.PSIsContainer} | Sort-Object {$_.FullName.Length} -Descending){
        Remove-Item -LiteralPath $entry.FullName
    }
    Remove-Item -LiteralPath $root
    $removed++
}
[pscustomobject]@{RemovedDirectories=$removed;FreedBytes=$freed;FreedGiB=[math]::Round($freed/1GB,2)}
