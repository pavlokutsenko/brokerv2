$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspace=Join-Path $repo 'workspace'
$fixture=Join-Path $workspace ('build-artifacts-smoke-'+[guid]::NewGuid().ToString('N'))
try {
    $scope=Join-Path $fixture 'scope';$tree=Join-Path $scope 'generated'
    [IO.Directory]::CreateDirectory((Join-Path $tree 'nested')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $tree 'nested/file.txt'),'generated')
    $outside=Join-Path $fixture 'outside.txt';[IO.File]::WriteAllText($outside,'preserve')
    $rejected=$false
    try { & (Join-Path $repo 'scripts/remove-generated-tree.ps1') -Path $outside -Root $scope }
    catch {$rejected=$true}
    if(-not $rejected -or -not (Test-Path -LiteralPath $outside)){throw 'Out-of-root file was not protected'}
    & (Join-Path $repo 'scripts/remove-generated-tree.ps1') -Path $tree -Root $scope
    if(Test-Path -LiteralPath $tree){throw 'Generated tree survived cleanup'}
    $app=Join-Path $fixture 'App';$recovery=$app+'.package-recovery'
    [IO.Directory]::CreateDirectory((Join-Path $app 'runtime')) | Out-Null
    $versions=@(1..3 | ForEach-Object {Join-Path $recovery ([guid]::NewGuid().ToString('N'))})
    foreach($v in $versions){[IO.Directory]::CreateDirectory($v) | Out-Null;[IO.File]::WriteAllText((Join-Path $v 'file.txt'),'old');(Get-Item -LiteralPath $v).CreationTime=(Get-Date).AddHours(-2)}
    $manifest=Join-Path $app 'runtime/.package-state.json'
    @{VersionPath=$versions[0]} | ConvertTo-Json | Set-Content -LiteralPath $manifest
    @{VersionPath=$versions[1]} | ConvertTo-Json | Set-Content -LiteralPath ($manifest+'.bak')
    & (Join-Path $repo 'scripts/prune-package-recovery.ps1') -Destination $app
    if(-not (Test-Path -LiteralPath $versions[0]) -or -not (Test-Path -LiteralPath $versions[1]) -or (Test-Path -LiteralPath $versions[2])){throw 'Recovery did not preserve current/previous manifests'}
    'BUILD_ARTIFACTS_OK bounded_delete unrelated_file_preserved current_previous_recovery_preserved'
} finally {
    & (Join-Path $repo 'scripts/remove-generated-tree.ps1') -Path $fixture -Root $workspace
}
