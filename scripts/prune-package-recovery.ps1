param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference='Stop'
$recovery=[IO.Path]::GetFullPath($Destination+'.package-recovery')
if(-not (Test-Path -LiteralPath $recovery)){return}
$retained=@()
foreach($layout in @('runtime','')){
    $manifest=Join-Path (Join-Path $Destination $layout) '.package-state.json'
    foreach($file in @($manifest,($manifest+'.bak'))){
        if(Test-Path -LiteralPath $file){
            $record=Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
            $version=[IO.Path]::GetFullPath($record.VersionPath)
            if(-not $version.StartsWith($recovery+'\',[StringComparison]::OrdinalIgnoreCase)){
                # Older isolated publications could copy a foreign backup.
                # Never follow it or prune its directory from this destination.
                if($file.EndsWith('.bak',[StringComparison]::OrdinalIgnoreCase)){continue}
                throw 'Recovery manifest escapes root'
            }
            $retained+=$version
        }
    }
}
if(-not $retained.Count){throw 'No recovery manifest; refuse pruning.'}
foreach($version in Get-ChildItem -LiteralPath $recovery -Directory){
    if($version.Name -notmatch '^[a-f0-9]{32}$' -or $version.FullName -in $retained -or
        $version.CreationTime -gt (Get-Date).AddMinutes(-10)){continue}
    & (Join-Path $PSScriptRoot 'remove-generated-tree.ps1') -Path $version.FullName -Root $recovery
}
