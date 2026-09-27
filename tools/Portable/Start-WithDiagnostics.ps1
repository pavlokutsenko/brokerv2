#requires -Version 5.1
$ErrorActionPreference = 'Stop'
try {
    # UAC and the driver's child loader use Windows PowerShell 5.1. Do not
    # propagate PowerShell 7's private module paths into those processes.
    $systemModules = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\Modules'
    $env:PSModulePath = $systemModules + ';' + [Environment]::GetEnvironmentVariable('PSModulePath', 'Machine')
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        $shell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        Start-Process -FilePath $shell -Verb RunAs -WindowStyle Hidden -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'))
        exit 0
    }
    if (Get-Process -Name 'PriceCheck.Collector' -ErrorAction SilentlyContinue) {
        throw 'Collector is already running. Close it before starting a diagnostic session.'
    }
    if (Get-Process -Name 'lu4','lu4.bin','lu4-win64-shipping' -ErrorAction SilentlyContinue) {
        throw 'Close LU4 clients on this PC before starting Collector and its driver for this test.'
    }
    & (Join-Path $PSScriptRoot 'Verify-Package.ps1')
    # Enable existing metadata only. Research flags must not leak into a portable test.
    Get-ChildItem Env: | Where-Object { $_.Name.StartsWith('PRICECHECK_TEST_') } |
        ForEach-Object { Remove-Item -LiteralPath ('Env:\' + $_.Name) }
    $env:PRICECHECK_TRACE_HARDWARE = '1'
    $packageRoot = Split-Path -Parent $PSScriptRoot
    Start-Process -FilePath (Join-Path $packageRoot 'PriceCheck.Collector.exe') -WorkingDirectory $packageRoot
}
catch {
    Add-Type -AssemblyName PresentationFramework
    [void][System.Windows.MessageBox]::Show($_.Exception.Message, 'PriceCheck diagnostic launch')
    exit 1
}
