# Локальная разработка Collector

Все команды выполняются из `C:\broker`. Обычный цикл после изменения кода:

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
