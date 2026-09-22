param(
    [ValidateSet('run', 'restart', 'start', 'stop', 'build', 'full', 'status', 'setup')]
    [string]$Command = 'run'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\dev-flow.ps1')

try {
    switch ($Command) {
        'run' {
            Stop-DevCollector
            Build-DevCollector
            Start-DevCollector
            Show-DevStatus
        }
        'restart' {
            Stop-DevCollector
            Assert-DevLoaderCompatible
            Start-DevCollector
            Show-DevStatus
        }
        'start' {
            Assert-DevLoaderCompatible
            Start-DevCollector
            Show-DevStatus
        }
        'stop' { Stop-DevCollector }
        'build' { Build-DevCollector }
        'full' {
            Stop-DevCollector
            Build-DevCollector -Full
            Start-DevCollector
            Show-DevStatus
        }
        'status' { Show-DevStatus }
        'setup' { Install-DevTask }
    }
}
catch {
    Write-Error $_
    exit 1
}
