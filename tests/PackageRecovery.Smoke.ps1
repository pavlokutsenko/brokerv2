$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$root = Join-Path $repo ('workspace\package-recovery-test-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'source'
$destination = Join-Path $root 'installed'
New-Item -ItemType Directory -Path $source -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $source 'PriceCheck.Collector.runtimeconfig.json'), '{"runtimeOptions":{"tfm":"net8.0"}}')
[IO.File]::WriteAllText((Join-Path $source 'PriceCheck.Collector.exe'), 'MZ-test-version-one')
$publisher = Join-Path $repo 'scripts\publish-durable.ps1'
& $publisher -Source $source -Destination $destination
[IO.File]::WriteAllBytes((Join-Path $destination 'PriceCheck.Collector.runtimeconfig.json'), (New-Object byte[] 468))
& $publisher -Destination $destination -Restore
$null = Get-Content -LiteralPath (Join-Path $destination 'PriceCheck.Collector.runtimeconfig.json') -Raw | ConvertFrom-Json
# Model interruption between replacing two files: committed version two must
# restore the old executable left alongside the new configuration.
[IO.File]::WriteAllText((Join-Path $source 'PriceCheck.Collector.exe'), 'MZ-test-version-two')
& $publisher -Source $source -Destination $destination
[IO.File]::WriteAllText((Join-Path $destination 'PriceCheck.Collector.exe'), 'MZ-test-version-one')
& $publisher -Destination $destination -Restore
if ([IO.File]::ReadAllText((Join-Path $destination 'PriceCheck.Collector.exe')) -ne 'MZ-test-version-two') { throw 'Interrupted publication recovery failed.' }
$stateBefore = [IO.File]::ReadAllText((Join-Path $destination '.package-state.json'))
[IO.File]::WriteAllBytes((Join-Path $source 'PriceCheck.Collector.runtimeconfig.json'), (New-Object byte[] 468))
$rejected = $false
try { & $publisher -Source $source -Destination $destination } catch { $rejected = $true }
if (-not $rejected -or [IO.File]::ReadAllText((Join-Path $destination '.package-state.json')) -ne $stateBefore) { throw 'Invalid source changed the installed package.' }
[IO.File]::WriteAllText((Join-Path $source 'PriceCheck.Collector.runtimeconfig.json'), '{"runtimeOptions":{"tfm":"net8.0"}}')
[IO.File]::WriteAllBytes((Join-Path $source 'PriceCheck.Collector.exe'), @())
$rejected = $false
try { & $publisher -Source $source -Destination $destination } catch { $rejected = $true }
if (-not $rejected -or [IO.File]::ReadAllText((Join-Path $destination '.package-state.json')) -ne $stateBefore) { throw 'Empty binary changed the installed package.' }
Write-Host 'PASS: zero-filled config recovery, interrupted installation recovery, invalid-source and empty-binary rejection.'
