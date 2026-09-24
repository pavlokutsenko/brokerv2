param([Parameter(Mandatory = $true)][string]$LogPath, [switch]$ForceReload)

$ErrorActionPreference = 'Stop'
$ServiceName = 'LU4Memory'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$DriverPath = Join-Path $Root 'lu4_memory_wfp.sys'
$KduPath = Join-Path $Root 'kdu.exe'

function Write-DriverLog {
    param([string]$Message)
    $line = '{0:yyyy-MM-dd HH:mm:ss.fff} {1}' -f (Get-Date), $Message
    Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
}

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Загрузчик LU4Memory не получил права администратора.'
    }
}

function Get-ServiceBinaryPath {
    $lines = & sc.exe qc $ServiceName 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    foreach ($line in $lines) {
        if ($line -match 'BINARY_PATH_NAME\s+:\s+(.*)$') { return $Matches[1].Trim().Trim('"') }
    }
    return $null
}

function Assert-FileHash {
    param([string]$Path, [string]$Expected)
    if (-not (Test-Path -LiteralPath $Path)) { throw "Отсутствует файл загрузчика: $Path" }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actual -ne $Expected) { throw "Контрольная сумма не совпала: $Path" }
}

try {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogPath) | Out-Null
    Set-Content -LiteralPath $LogPath -Value '' -Encoding UTF8
    Assert-Administrator
    Assert-FileHash $DriverPath 'C7228FD5D285C29268A64707B3062FEEEFCCB76BEB5332CB8BF5B4E52CCC64DA'
    Assert-FileHash $KduPath 'EA626A9FF0F0A6FD0BB8377AFA93C6ECBFB388204C3ED4E90D7000CDEDC25B1D'
    Assert-FileHash (Join-Path $Root 'drv64.dll') '155E357D76874EA8D203643F63C9066A3DFD3DAB3E150E4735FBAD673EF06F7F'
    Assert-FileHash (Join-Path $Root 'Taigei64.dll') '8A48F93D2A8121A8E4F4BF76378D54EC40184987EB16B795614142C8E3E2BDA1'

    $existingPath = Get-ServiceBinaryPath
    $state = Get-CimInstance Win32_SystemDriver -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue
    if ($state -and $state.State -eq 'Running') {
        $normalized = $existingPath
        if ($normalized -and $normalized.StartsWith('\??\')) { $normalized = $normalized.Substring(4) }
        if (-not $normalized -or -not (Test-Path -LiteralPath $normalized) -or
            (Get-FileHash -LiteralPath $normalized -Algorithm SHA256).Hash -notin @(
                '2393EEE0E77E03A3C5D4640CE16F0A6AC1B6DE1E1A3D7887B1635AE6186AE766',
                'C7228FD5D285C29268A64707B3062FEEEFCCB76BEB5332CB8BF5B4E52CCC64DA')) {
            throw 'Служба LU4Memory уже запущена из другого или повреждённого бинарника. Закройте игровые клиенты и перезагрузите Windows.'
        }
        if (-not $ForceReload) {
            Write-DriverLog 'LU4Memory уже запущен.'
            exit 0
        }
        if (Get-Process -Name 'lu4','lu4.bin','lu4-win64-shipping' -ErrorAction SilentlyContinue) {
            throw 'Закройте клиенты LU4 перед обновлением LU4Memory.'
        }
        Write-DriverLog 'Останавливаю прежний LU4Memory для обновления.'
        & sc.exe stop $ServiceName | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Не удалось остановить прежний LU4Memory.' }
        $stopped = $false
        for ($attempt = 0; $attempt -lt 50; $attempt++) {
            $current = Get-CimInstance Win32_SystemDriver -Filter "Name='$ServiceName'" -ErrorAction Stop
            if ($current.State -eq 'Stopped') { $stopped = $true; break }
            Start-Sleep -Milliseconds 200
        }
        if (-not $stopped) { throw 'Прежний LU4Memory не остановился за 10 секунд.' }
    }

    if ($existingPath) {
        $normalized = $existingPath
        if ($normalized.StartsWith('\??\')) { $normalized = $normalized.Substring(4) }
        if (-not [string]::Equals([IO.Path]::GetFullPath($normalized), [IO.Path]::GetFullPath($DriverPath), [StringComparison]::OrdinalIgnoreCase)) {
            & sc.exe config $ServiceName binPath= $DriverPath | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Не удалось обновить путь службы LU4Memory.' }
        }
    }

    $originalDseValue = $null
    Push-Location $Root
    try {
        Write-DriverLog 'Временно отключаю проверку подписи через проверенный KDU provider 1.'
        $disableOutput = @(& $KduPath -dse 0 -prv 1 2>&1)
        if ($LASTEXITCODE -notin 0, 1) { throw "KDU не изменил DSE, код $LASTEXITCODE." }
        $dseLine = $disableOutput | Where-Object { $_ -match 'DSE flags .* value:\s*([0-9A-Fa-f]+),\s*new value' } | Select-Object -First 1
        if (-not $dseLine -or $dseLine -notmatch 'value:\s*([0-9A-Fa-f]+),\s*new value') {
            throw 'KDU не сообщил исходное значение DSE; загрузка отменена.'
        }
        $originalDseValue = [Convert]::ToUInt32($Matches[1], 16)

        if (-not $existingPath) {
            & sc.exe create $ServiceName binPath= $DriverPath type= kernel start= demand | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Не удалось создать службу LU4Memory.' }
        }
        & sc.exe start $ServiceName | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Не удалось запустить службу LU4Memory.' }
    }
    finally {
        try {
            if ($null -ne $originalDseValue) {
                Write-DriverLog ('Восстанавливаю исходное DSE 0x{0:X}.' -f $originalDseValue)
                & $KduPath -dse $originalDseValue -prv 1 | Out-Null
                if ($LASTEXITCODE -notin 0, 1) { throw "KDU не восстановил DSE, код $LASTEXITCODE." }
            }
        }
        finally { Pop-Location }
    }

    $state = Get-CimInstance Win32_SystemDriver -Filter "Name='$ServiceName'" -ErrorAction Stop
    if ($state.State -ne 'Running') { throw 'LU4Memory не перешёл в состояние Running.' }
    Write-DriverLog "LU4Memory запущен из $DriverPath."
    exit 0
}
catch {
    try { Write-DriverLog ("ОШИБКА: " + $_.Exception.Message) } catch { }
    exit 1
}
