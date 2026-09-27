# Launcher и Collector — портативные поставки

Финальные ZIP от 27 сентября собраны из чистых выходов проверенной сборки
`efb2a79169f2491ea7a71e310b254b49` и содержат текущие проверки HWID/маршрута.
В `release/packages` оставлены только два финальных ZIP и их `.sha256`;
старые архивы и распакованный пакет перенесены в
`workspace/obsolete-packages-67d2b901`, поскольку автоматическая проверка
отклонила их физическое удаление. Успешный игровой вход и смена персонажа
описаны в [LAUNCH_PROTECTION.md](LAUNCH_PROTECTION.md).

Один репозиторий, общие библиотеки, два WPF-приложения:

| Приложение | Назначение | Зависимости |
| --- | --- | --- |
| PriceCheck.Launcher | Игровые окна, HWID, HTTP-прокси, автовход, ротация, перезапуск | Launching.UI → Launching → Contracts / Windows |
| PriceCheck.Collector | Тот же запуск и полноценный сбор рынка / цен | Launching.UI + Collection → Contracts / Windows |

`Launching.UI` содержит общие LaunchPanelView, LaunchTemplatesView и стили.
Код запуска, native-agent, автовход и загрузчик драйвера общие. Launcher не
ссылается на Collector или Collection, не создаёт радар, outbox, SQLite,
BrokerWorker или HTTP-клиент API рынка. Native-автовход сам сообщает список
персонажей для ротации; reader для этого Launcher не требуется. Перезапуск
Launcher наблюдает живой процесс, окно и TCP без чтения игрового мира.

Общие модели и namespaces с прежним именем Collector сохранены для
совместимости JSON и существующих тестов; это не зависимость от сборщика.
Сервер из C:\PriceCheck отдельно: ему не нужны изменения для этого разделения.

## Сборка

Из C:\broker в PowerShell 7:

```powershell
.\build.ps1
```

Скрипт пересобирает изменённые native-компоненты и BrokerWorker, публикует обе
программы с Windows x64 .NET/WPF runtime, затем создаёт **два** ZIP и соседние
`.sha256`. Обе программы имеют один BuildId. Выходы:

- `release\PriceCheckLauncher\PriceCheck.Launcher.exe`
- `release\PriceCheckCollector\PriceCheck.Collector.exe`
- `release\packages\PriceCheckLauncher-win-x64-<дата-время>.zip`
- `release\packages\PriceCheckCollector-win-x64-<дата-время>.zip`

`-SkipPackages` пропускает только ZIP. `-OutputDirectory` меняет каталог
Collector, `-LauncherOutputDirectory` — Launcher, `-PackageDirectory` — архивы.
Сборка не запускает приложения или игры. Закройте программу перед обновлением
её установленного release; durable-публикация откажется заменять живой процесс.
Архивы собираются из свежих staging-каталогов, а не из пользовательского release.

Для отдельной упаковки уже опубликованного чистого staging:

```powershell
.\scripts\package-portable.ps1 -Product Launcher -PublishDirectory <папка>
.\scripts\package-portable.ps1 -Product Collector -PublishDirectory <папка>
```

Без PublishDirectory команда сама собирает обе программы в отдельные каталоги
и упаковывает выбранную. Серверная поставка и параметр IncludeServer удалены:
используется отдельный сервер из C:\PriceCheck, Collector подключается к его API.

## Запуск на другом ПК

1. Выберите Launcher для ПК только с игровыми окнами или Collector для ПК со сбором.
2. **Полностью распакуйте** ZIP в отдельную папку. EXE находится сразу в корне,
   дополнительной вложенной папки PriceCheckLauncher / PriceCheckCollector нет.
3. Запустите EXE и подтвердите стандартный UAC. Игра устанавливается отдельно.
   Не запускайте из ZIP и не переносите один EXE без DLL и runtime-каталогов.
4. Укажите путь игры, настройте и сохраните HWID/прокси-шаблон, выберите его
   в профиле. Автовход и ротация проверены для Gamma. Прокси требует учётные данные.
5. В Collector задайте URL API; текущий default — https://pog-sandbox.com/api.
   Запустите клиент и включите сбор. Отдельный Launcher на таком ПК не нужен.

.NET, Python и исследовательские репозитории на целевом ПК не нужны. В Launcher
нет BrokerWorker/Python; Collector включает их. Обе поставки включают
ClientAgent, ClientLogin, проверенный LU4Memory и загрузчик: world identity
по-прежнему требует драйвер. Это разделение не меняет поддержку игровых сборок.

## Настройки и обновления

- Launcher: `%LOCALAPPDATA%\PriceCheckLauncher`.
- Collector: `%LOCALAPPDATA%\PriceCheckCollector` (прежний путь сохранён).

Каждая оболочка — единственный writer своего каталога. Второй экземпляр той же
оболочки, даже из другой папки, не запускается; отдельный upload worker Collector
разрешён. Launcher автоматически не читает и не переписывает настройки Collector.
Несколько профилей Launcher могут запускать клиентов одного сервера; каждый
получает отдельный новый PID. Профили Collector сохраняют прежние ограничения рынка.

Для переноса настроек при закрытых программах копируйте **оба** файла:
`profiles.json` и `launch-templates.json`. Профильные ID, template ID и world seed
сохраняйте: они участвуют в идентичности. На другом ПК заново введите пароли
игры и прокси: DPAPI привязан к исходной учётной записи/компьютеру. Launcher
не меняет сохранённую идентичность; выбранная ротация HWID остаётся осознанной
настройкой шаблона. Не используйте скопированные идентичные профили одновременно
как разные игровые окна. Старые PID при запуске не восстанавливаются.

Для обновления штатно закройте оболочку: её игры и прокси завершатся. Распакуйте
новый архив в новую папку и запустите EXE; LocalAppData останется. В Collector
сохраняются центр, локальная история и outbox. Не переписывайте работающий пакет.

## Проверка поставки

Каждый ZIP содержит README-FIRST.txt, build-info.json, package-manifest.json
и Verify-Package.ps1. Manifest schema 2 указывает продукт, корневой entry point,
размер и SHA256 каждого файла. Проверяется self-contained runtime и нужные
native-компоненты; Launcher отвергает примесь сборщика. Проверка из распакованной папки:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Verify-Package.ps1
```

В ZIP не входят профили, пароли, результаты сбора, журналы, серверная база или
сама игра. Сборка и smoke на текущем компьютере не заменяют запуск на чистой ОС;
чистая Windows VM здесь недоступна, загрузка драйвера на ней не проверена.

## Готовая сборка 27 сентября 2026

BuildId обоих приложений: `efb2a79169f2491ea7a71e310b254b49`.
Исполняемые файлы, native-agent и драйвер совпадают с финальным live-прогоном;
при упаковке приложения не пересобирались и пользовательские настройки не читались.

| Архив в release/packages | Размер | Manifest-файлы |
| --- | ---: | ---: |
| PriceCheckLauncher-win-x64-20260927-143033.zip | 75 035 745 байт | 479 |
| PriceCheckCollector-win-x64-20260927-143038.zip | 102 002 101 байт | 729 |

SHA256 Launcher: `BAD041963733BD0E33E69967D448EE572AAF00620556BCCFE8C9AE6B966E0F39`.
SHA256 Collector: `093F2E15D3B04D7C3BC9CA727CD2E88BEE37E4889952A685950341ADD0BDD184`.

Производственная сборка проверена через `build.ps1 -SkipPackages`,
Launcher.Smoke, CharacterRotation.UiSmoke, ModuleIsolation.Smoke,
LaunchProtection.Smoke --wfp и native guard probes. Финальный live-прогон
`workspace/launch-protection-live/attempt-13-final-build.log` подтвердил вход
двух персонажей в мир, подмену world identity, прокси и остановку при отзыве
маршрута. Подробности и границы проверки: [LAUNCH_PROTECTION.md](LAUNCH_PROTECTION.md).

Оба финальных ZIP независимо распакованы: DesktopPackages.Smoke подтвердил
EXE в корне, все manifest-хэши в Windows PowerShell 5.1, self-contained runtime,
отсутствие пользовательского состояния и отказ при повреждённой DLL того же
размера. Launcher не содержит сборщик/Python. Распаковка для проверки:
`workspace/desktop-packages-smoke-a453d65bfa1042ba815dbf8f314d046e`.
Упаковка не запускала игры; запуск на втором ПК/чистой Windows не проверен.
