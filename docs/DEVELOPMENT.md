# Локальная разработка Launcher и Collector

Все команды выполняются в PowerShell 7 из C:\broker.
Сервер и сайт находятся в C:\PriceCheck\market и собираются отдельно.

## Сборка без запуска игры

```powershell
cd C:\broker
.\build.ps1
```

build.ps1 проверяет необходимость пересборки BrokerWorker/native-компонентов,
публикует оба self-contained приложения и автоматически создаёт два ZIP с
единственным файлом EXE в корне и папкой runtime. Результаты: release\PriceCheckLauncher,
release\PriceCheckCollector и release\packages. Оба продукта получают один BuildId.
Устройство, параметры, перенос и проверенные архивы:
[DESKTOP_PACKAGES.md](DESKTOP_PACKAGES.md).

`scripts/layout-portable.ps1` создаёт стандартный SDK apphost с относительным
путём runtime\PriceCheck.<Product>.dll и переносит зависимости в runtime.
Действующий процесс остаётся корневым EXE; AppContext.BaseDirectory указывает
на runtime. build-info.json и recovery metadata также находятся в runtime.
Проверка ZIP, UAC manifest и запуска корневых EXE из распакованных папок:

```powershell
.\tests\DesktopPackages.Smoke.ps1 -Archives @(Get-ChildItem .\release\packages\*.zip | ForEach-Object FullName)
.\tests\PackageRecovery.Smoke.ps1
```

-SkipPackages пропускает ZIP, -ForceBroker всегда пересобирает BrokerWorker.
-SkipBroker использует уже подготовленный runtime; на чистом checkout сначала
нужна обычная сборка. Runtime стороннего сервера не требуется.

Если приложение открыто, используйте отдельные выходные каталоги **обоих**
продуктов, чтобы не перезаписать загруженные DLL:

```powershell
.\build.ps1 -SkipPackages `
    -OutputDirectory workspace\verify-collector `
    -LauncherOutputDirectory workspace\verify-launcher
```

## Цикл разработки с запуском Collector

```powershell
.\dev.ps1 run
```

Команда останавливает прежний Collector/upload worker, вызывает build.ps1
для обеих оболочек и ZIP, проверяет загрузчик Windows PowerShell 5.1,
запускает Collector через задачу PriceCheck Collector Standalone
(RunLevel=Highest) и ждёт основное окно/LU4Memory RUNNING. Launcher этой
командой не запускается. Закрытие владеющего Collector завершает его игры.

| Команда | Назначение |
| --- | --- |
| .\dev.ps1 build | Сборка обеих оболочек/ZIP без запуска |
| .\dev.ps1 run | Сборка, запуск и проверка Collector |
| .\dev.ps1 full | То же с принудительной пересборкой BrokerWorker |
| .\dev.ps1 restart | Перезапуск готового Collector без сборки |
| .\dev.ps1 start | Запуск готового Collector |
| .\dev.ps1 stop | Остановить Collector и upload worker |
| .\dev.ps1 status | Процессы, задача, состояние драйвера и bootstrap log |
| .\dev.ps1 setup | Настроить dev-задачу из elevated PowerShell |

dev.ps1 start/restart восстанавливают повреждённые файлы по package manifest
перед запуском. Bootstrap log:
%LOCALAPPDATA%\PriceCheckCollector\logs\driver-bootstrap.log.
Обновление не сбрасывает LocalAppData и не меняет saved center/geometry.

Launcher запускается напрямую:
release\PriceCheckLauncher\PriceCheck.Launcher.exe.
Для обычных пользователей переносимого ZIP задача разработчика не требуется.

## Изолированные проверки

Выбирайте проверки по изменённым компонентам; команды ниже не запускают игру.

```powershell
dotnet run --project tests/Launcher.Smoke -c Release -- workspace\launcher-ui
dotnet run --project tests/CollectorUi.Smoke -c Release -- workspace\collector-ui\templates.png
dotnet run --project tests/ModuleIsolation.Smoke -c Release
dotnet run --project tests/StorageRecovery.Smoke -c Release
dotnet run --project tests/LaunchProtection.Smoke/LaunchProtection.Smoke.csproj -c Release
.\tests\LaunchProtection.Smoke\test-native.ps1
```

UI smoke использует изолированные настройки и проверяет неизменность
пользовательских JSON. Проверка готовых ZIP и manifests:
tests/DesktopPackages.Smoke.ps1 (оба ZIP передаются явно).
tests/PortablePrune.Smoke.ps1 принимает один готовый ZIP и проверяет, что
неверный checksum сохраняет старые архивы, а успешная проверка удаляет только
старые ZIP/sha256 этого продукта. Пользовательские распаковки сохраняются.
Опциональный --wfp в LaunchProtection.Smoke требует загруженный новый LU4Memory
и elevated PowerShell; использует только тестовые процессы и локальные endpoint.
Native watchdog probe проверяет завершение тестового процесса. Полный охват
и отдельная игровая приёмка: [LAUNCH_PROTECTION.md](LAUNCH_PROTECTION.md).
Текущие выполненные проверки и ограничения перечислены в
[DESKTOP_PACKAGES.md](DESKTOP_PACKAGES.md).

Живые tests/*.LiveSmoke и исследовательские проходы управляют настоящей игрой.
Статус игры и оставшаяся приёмка: [RESUME.md](RESUME.md).
HWID/world identity зависят от сохранённых profile ID и WorldIdentitySeed;
Regenerate меняет seed. Поддержка привязана к проверенной версии клиента.
Подробности: [TwoClientIsolation](../tools/TwoClientIsolation/README.md),
[границы модулей](analysis/module-separation/README.md).
