# Локальная разработка Collector

Все команды выполняются из `C:\broker`. Обычный цикл после изменения кода:

Наблюдение текущей пары остановлено пользователем 24 сентября в 15:13 по Киеву;
автоматизация `gamma` приостановлена. Игры оставлены открытыми. Перед
`dev.ps1 run/restart` перепроверьте живые процессы и учитывайте потерю их прокси
при завершении владеющего Collector.
Рефакторинг двух модулей опубликован отдельно в
`workspace\module-separation-publish`; рабочий release остаётся прежним.
Проверки модулей: `dotnet run --project tests/ModuleIsolation.Smoke -c Release`;
визуальный smoke: `dotnet run --project tests/CollectorUi.Smoke -c Release --
workspace\module-ui\templates.png`. Последний явно отключает WPF startup и
проверяет неизменность файлов пользовательских настроек. См.
[результаты и ограничения](analysis/module-separation/README.md).

```powershell
.\dev.ps1 run
```

Команда последовательно:

1. останавливает основное окно Collector и его `--upload-worker`;
2. пересобирает BrokerWorker только при изменении его исходников;
3. публикует self-contained WPF-приложение в `release\PriceCheckCollector`;
4. проверяет загрузчик парсером штатного Windows PowerShell 5.1;
5. запускает Collector через задачу `PriceCheck Collector Standalone` с
   `RunLevel=Highest`;
6. ждёт основное окно и `RUNNING` у службы LU4Memory либо возвращает ошибку и
   хвост bootstrap-лога.

Доступные команды:

| Команда | Назначение |
| --- | --- |
| `.\dev.ps1 run` | Инкрементальная сборка, запуск и проверка |
| `.\dev.ps1 restart` | Быстрый перезапуск текущей portable-сборки |
| `.\dev.ps1 start` | Запуск без остановки и сборки |
| `.\dev.ps1 stop` | Остановить Collector и upload worker; клиент LU4 и драйвер не трогаются |
| `.\dev.ps1 build` | Только инкрементальная сборка |
| `.\dev.ps1 full` | Полная пересборка BrokerWorker, публикация, запуск и проверка |
| `.\dev.ps1 status` | Состояние задачи, процессов, драйвера, путь SYS и последний лог |
| `.\dev.ps1 setup` | Переустановить dev-задачу; требует elevated PowerShell |

`build.ps1` остаётся низкоуровневой командой CI/публикации. Без параметров он
тоже проверяет время изменения входов BrokerWorker и пропускает PyInstaller,
если staged runtime актуален. `build.ps1 -ForceBroker` всегда пересобирает
worker, а `build.ps1 -SkipBroker` публикует только WPF-часть.
Для проверки сборки при открытом клиенте используйте
`build.ps1 -SkipBroker -OutputDirectory workspace\build-verify`: загруженные
клиентом DLL не позволяют перезаписать обычный каталог `release`.
Временные каталоги PyInstaller `stage` и `dist` удаляются после успешного
копирования runtime в WPF-проект.

Первичная настройка нужна только при отсутствии задачи или изменении её пути:

```powershell
# elevated PowerShell
cd C:\broker
.\dev.ps1 setup
```

После этого `run`, `restart` и `status` не требуют ручного поиска процессов,
запуска загрузчика или проверки `sc.exe`. Основное окно появляется только после
того, как его собственный bootstrap открыл `\\.\LU4Memory`, получил base
процесса и прочитал сигнатуру `MZ`, поэтому успешный `ready` подтверждает не
только статус службы, но и рабочий IOCTL.

Диагностический лог драйвера:

```text
%LOCALAPPDATA%\PriceCheckCollector\logs\driver-bootstrap.log
```

После проверки 2026-09-24 штатный запуск HWID-профиля также задаёт отдельный
world identity из сохранённого ID профиля и `WorldIdentitySeed` шаблона.
Regenerate меняет seed, сохраняя ID профиля; общий шаблон по-прежнему даёт
разные значения двум профилям. Старые шаблоны без seed сохраняют прежнее
значение до регенерации. Результат без секретов записывается
в `%LOCALAPPDATA%\PriceCheckCollector\logs\world-identity-<PID>.txt`.
Поддержка привязана к проверенной версии клиента/античита; после обновления
нужно заново проверить guards и одиночный/парный вход. Подробности и read-only
проверка двух профилей: `tools/TwoClientIsolation/README.md`.

Для последовательного запуска двух сохранённых профилей одним Collector:

```powershell
Start-Process -FilePath 'C:\broker\release\PriceCheckCollector\PriceCheck.Collector.exe' `
    -ArgumentList '--launch-profiles=Gamma,Black'
```

Второй профиль запускается после получения позиции первого в мире.
Этот аргумент следует использовать при старте Collector; не запускать
дополнительный экземпляр приложения поверх уже работающего.

Для контрольного восстановления конкретного профиля после запуска приложения
можно передать `--launch-profile=<имя>` напрямую portable EXE. Collector
загружает сохранённые профили и шаблоны, затем запускает выбранный профиль тем
же путём, что и кнопка `Launch client`. Если профиль уже владеет живым PID,
повторный запуск пропускается. Пример:

```powershell
Start-Process -FilePath 'C:\broker\release\PriceCheckCollector\PriceCheck.Collector.exe' `
    -WorkingDirectory 'C:\broker\release\PriceCheckCollector' `
    -ArgumentList '--launch-profile=Gamma'
```
