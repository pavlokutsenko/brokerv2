$ErrorActionPreference = 'Stop'

$script:DevTaskName = 'PriceCheck Collector Standalone'
$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:ReleaseDirectory = Join-Path $script:RepoRoot 'release\PriceCheckCollector'
$script:CollectorExe = Join-Path $script:ReleaseDirectory 'PriceCheck.Collector.exe'
$script:DriverLoader = Join-Path $script:ReleaseDirectory 'DriverRuntime\load-driver.ps1'
$script:BootstrapLog = Join-Path $env:LOCALAPPDATA 'PriceCheckCollector\logs\driver-bootstrap.log'

function Test-DevAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Stop-DevCollector {
    $task = Get-ScheduledTask -TaskName $script:DevTaskName -ErrorAction SilentlyContinue
    if ($task -and $task.State -eq 'Running') {
        Stop-ScheduledTask -TaskName $script:DevTaskName
    }
    Get-Process -Name 'PriceCheck.Collector' -ErrorAction SilentlyContinue | Stop-Process -Force
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while ((Get-Process -Name 'PriceCheck.Collector' -ErrorAction SilentlyContinue) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }
    Write-Host 'Collector: stopped.'
}

function Test-DevTaskCurrent {
    $task = Get-ScheduledTask -TaskName $script:DevTaskName -ErrorAction SilentlyContinue
    if (-not $task) { return $false }
    $action = @($task.Actions)[0]
    if (-not $action) { return $false }
    $expectedExe = [IO.Path]::GetFullPath($script:CollectorExe)
    $expectedDirectory = [IO.Path]::GetFullPath($script:ReleaseDirectory).TrimEnd('\')
    $actualExe = [Environment]::ExpandEnvironmentVariables([string]$action.Execute).Trim('"')
    $actualDirectory = [Environment]::ExpandEnvironmentVariables([string]$action.WorkingDirectory).TrimEnd('\')
    return [string]::Equals($expectedExe, $actualExe, [StringComparison]::OrdinalIgnoreCase) -and
        [string]::Equals($expectedDirectory, $actualDirectory, [StringComparison]::OrdinalIgnoreCase) -and
        $task.Principal.RunLevel -eq 'Highest'
}

function Install-DevTask {
    if (-not (Test-Path -LiteralPath $script:CollectorExe)) {
        throw "Collector executable is missing: $script:CollectorExe"
    }
    if (-not (Test-DevAdministrator)) {
        throw 'Run .\dev.ps1 setup once from an elevated PowerShell window.'
    }

    $action = New-ScheduledTaskAction -Execute $script:CollectorExe -WorkingDirectory $script:ReleaseDirectory
    $principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) `
        -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $script:DevTaskName -Action $action -Principal $principal `
        -Settings $settings -Force | Out-Null
    Write-Host "Scheduled task: installed for $script:CollectorExe"
}

function Assert-DevLoaderCompatible {
    if (-not (Test-Path -LiteralPath $script:DriverLoader)) {
        throw "Bundled driver loader is missing: $script:DriverLoader"
    }
    $powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $escaped = $script:DriverLoader.Replace("'", "''")
    $command = "`$tokens=`$null;`$errors=`$null;[System.Management.Automation.Language.Parser]::ParseFile('$escaped',[ref]`$tokens,[ref]`$errors)|Out-Null;if(`$errors.Count){`$errors|ForEach-Object{Write-Error `$_.ToString()};exit 1}"
    & $powershell -NoProfile -Command $command
    if ($LASTEXITCODE -ne 0) { throw 'Windows PowerShell 5.1 cannot parse the bundled driver loader.' }
}

function Build-DevCollector {
    param([switch]$Full)
    Push-Location $script:RepoRoot
    try {
        if ($Full) { & (Join-Path $script:RepoRoot 'build.ps1') -ForceBroker }
        else { & (Join-Path $script:RepoRoot 'build.ps1') }
        if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
    }
    finally { Pop-Location }
    Assert-DevLoaderCompatible
    Write-Host 'Build: ready.'
}

function Start-DevCollector {
    if (-not (Test-Path -LiteralPath $script:CollectorExe)) {
        throw 'Portable build is missing. Run .\dev.ps1 run or .\dev.ps1 full.'
    }
    if (-not (Test-DevTaskCurrent)) {
        Install-DevTask
    }
    & (Join-Path $script:RepoRoot 'scripts\publish-durable.ps1') -Destination $script:ReleaseDirectory -Restore
    Start-ScheduledTask -TaskName $script:DevTaskName
    Write-Host 'Collector: starting and checking driver bootstrap...'

    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        Start-Sleep -Milliseconds 250
        $process = Get-Process -Name 'PriceCheck.Collector' -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
        if ($process -and $process.MainWindowTitle -like 'Не удалось*') {
            $tail = if (Test-Path -LiteralPath $script:BootstrapLog) {
                (Get-Content -LiteralPath $script:BootstrapLog -Tail 8) -join [Environment]::NewLine
            } else { 'Driver bootstrap log was not created.' }
            throw "Collector reported a startup failure.`n$tail"
        }
        $driver = Get-CimInstance Win32_SystemDriver -Filter "Name='LU4Memory'" -ErrorAction SilentlyContinue
        if ($process -and $process.MainWindowTitle -eq 'PriceCheck Collector' -and $driver.State -eq 'Running') {
            Write-Host "Collector: ready (PID $($process.Id)); LU4Memory: RUNNING."
            return
        }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Collector did not become ready within 60 seconds.'
}

function Show-DevStatus {
    $task = Get-ScheduledTask -TaskName $script:DevTaskName -ErrorAction SilentlyContinue
    $processes = @(Get-CimInstance Win32_Process -Filter "Name='PriceCheck.Collector.exe'" -ErrorAction SilentlyContinue)
    $root = @($processes | Where-Object { $_.CommandLine -notmatch '--upload-worker' })
    $workers = @($processes | Where-Object { $_.CommandLine -match '--upload-worker' })
    $driver = Get-CimInstance Win32_SystemDriver -Filter "Name='LU4Memory'" -ErrorAction SilentlyContinue
    $servicePath = $null
    $serviceLines = & sc.exe qc LU4Memory 2>$null
    foreach ($line in $serviceLines) {
        if ($line -match 'BINARY_PATH_NAME\s+:\s+(.*)$') { $servicePath = $Matches[1].Trim(); break }
    }
    $lastLog = if (Test-Path -LiteralPath $script:BootstrapLog) {
        Get-Content -LiteralPath $script:BootstrapLog | Where-Object { $_ } | Select-Object -Last 1
    } else { 'нет' }

    [pscustomobject]@{
        Task = if ($task) { [string]$task.State } else { 'NOT_INSTALLED' }
        TaskCurrent = Test-DevTaskCurrent
        CollectorPids = ($root.ProcessId -join ', ')
        UploadWorkerPids = ($workers.ProcessId -join ', ')
        DriverState = if ($driver) { $driver.State } else { 'NOT_INSTALLED' }
        DriverPath = $servicePath
        LastBootstrapLog = $lastLog
    } | Format-List
}
