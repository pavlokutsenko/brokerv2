# Launcher и Collector — портативные поставки

Финальные ZIP от 27 сентября собраны из чистых выходов проверенной сборки
`d013b2d8cda841608edae1fd680d25a5` и содержат текущие проверки HWID/маршрута.
В корне каждого ZIP один EXE и папка `runtime`; DLL, локализации, драйвер,
README и проверочные скрипты находятся внутри `runtime`.
В `release/packages` два финальных ZIP и их `.sha256`; ранняя пользовательская
распаковка Launcher сохранена. Новые сборки удаляют старые ZIP/sha256 того же продукта;
предыдущие плоские архивы и распакованный пакет перенесены в
`workspace/obsolete-flat-packages-20260927`. Более ранние пакеты находятся в
`workspace/obsolete-packages-67d2b901`: автоматическая проверка отклонила
физическое удаление. Успешный игровой вход и смена персонажа
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

После проверки новой поставки удаляются прежние ZIP и `.sha256` того же
продукта в выбранном PackageDirectory. До успешного создания, проверки SHA256
и структуры нового ZIP прежние архивы сохраняются. Распакованные пользователем
папки и посторонние файлы не удаляются.

`-SkipPackages` пропускает только ZIP. `-OutputDirectory` меняет каталог
Collector, `-LauncherOutputDirectory` — Launcher, `-PackageDirectory` — архивы.
Сборка не запускает приложения или игры. Закройте программу перед обновлением
её установленного release; durable-публикация откажется заменять живой процесс.
Архивы собираются из свежих staging-каталогов, а не из пользовательского release.
Корневой EXE создаётся стандартной задачей SDK CreateAppHost и запускает
runtime\PriceCheck.<Product>.dll в том же процессе. DLL и runtime не извлекаются
во временную папку при запуске; внешний .NET не нужен.

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
   Рядом только папка runtime со всеми служебными файлами.
3. Запустите EXE и подтвердите стандартный UAC. Игра устанавливается отдельно.
   Не запускайте из ZIP; переносите EXE вместе с папкой runtime.
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

В runtime каждого ZIP находятся README-FIRST.txt, build-info.json,
package-manifest.json и Verify-Package.ps1. Manifest schema 3 указывает продукт,
корневой entry point, каталог runtime,
размер и SHA256 каждого файла. Проверяется self-contained runtime и нужные
native-компоненты; Launcher отвергает примесь сборщика. Проверка из распакованной папки:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\runtime\Verify-Package.ps1
```

В ZIP не входят профили, пароли, результаты сбора, журналы, серверная база или
сама игра. Сборка и smoke на текущем компьютере не заменяют запуск на чистой ОС;
чистая Windows VM здесь недоступна, загрузка драйвера на ней не проверена.

## Готовая сборка 27 сентября 2026

BuildId обоих приложений: `d013b2d8cda841608edae1fd680d25a5`.
Приложения пересобраны с новым расположением файлов и необязательным прокси.
Agent/автовход поддерживают HWID-only lease (flags 1) и HWID+proxy (flags 3).
Драйвер и BrokerWorker не менялись. HWID из шаблона проверяется в обоих режимах.

| Архив в release/packages | Размер | Manifest-файлы |
| --- | ---: | ---: |
| PriceCheckLauncher-win-x64-20260927-151939.zip | 75 045 566 байт | 479 |
| PriceCheckCollector-win-x64-20260927-151945.zip | 102 015 946 байт | 729 |

SHA256 Launcher: `F3ECF3E4B6701B7AE83C0FD2E550D8FA58C25F0EC4817B3061F5BB56844018F7`.
SHA256 Collector: `4BE9EB19796B9318E8641E789BB1D59A4E1892D722EC75797BFF02EBC8EE31C0`.

Текущая защита проверена через Launcher.Smoke, CharacterRotation.UiSmoke,
ModuleIsolation.Smoke, LaunchProtection.Smoke --wfp и native guard probes.
Два полных live-прогона `workspace/launch-protection-live/optional-proxy-direct.log`
и `optional-proxy-enabled.log` подтвердили вход слотов 0/1, подмену HWID,
direct/CONNECT трафик, непрерывные проверки, ротацию и остановку при отзыве
своего маршрута. Настройки на диске не изменились. Подробности и границы проверки:
[LAUNCH_PROTECTION.md](LAUNCH_PROTECTION.md).

Новый формат собран через `build.ps1`. Оба ZIP независимо распакованы:
DesktopPackages.Smoke подтвердил единственный файл EXE в корне, одну папку runtime,
GUI/UAC manifest, все manifest-хэши в Windows PowerShell 5.1, self-contained runtime,
отсутствие пользовательского состояния и отказ при повреждённой DLL того же
размера. Launcher не содержит сборщик/Python. Распаковка для проверки:
`workspace/desktop-packages-smoke-55e0e1f74a2b49b9b7101351d3d84539`.
PortableHost.Smoke запустил каждый корневой EXE с тестовым startup hook,
загрузил реальные WPF-окна без игрового runtime, подтвердил путь runtime и
работу из другой рабочей папки при недоступном глобальном .NET. Настройки не
изменились. PackageRecovery.Smoke подтвердил восстановление EXE/config и
хранение recovery metadata внутри runtime. PortablePrune.Smoke подтвердил
сохранение старых архивов при неверном checksum и удаление только прежних
ZIP/sha256 после успешной проверки. Проверка упаковки не запускала игры;
полные входы проверены отдельно. Запуск на втором ПК/чистой Windows не проверялся.
