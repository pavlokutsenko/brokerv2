# Производительность сайта — проверено 27 сентября 2026

Развёрнут релиз **77e3d36a41ea94d26831618e568ec5a0d235423b**.
[CI/deploy 36305897797](https://github.com/pavlokutsenko/pricecheck-market/actions/runs/36305897797)
и [production audit 36306313541](https://github.com/pavlokutsenko/pricecheck-market/actions/runs/36306313541)
прошли. Каталог, Iron Ore (item 1869), торговцы и все семь страниц профита
проверены в пользовательском авторизованном Chrome. Ожидаемые counts
отрисовались без ошибок API и зависшего loading.

## Измерения браузера

| Страница/API | Время | Результат |
| --- | ---: | --- |
| Iron Ore detail / prices | 174–283 / 227–344 мс | 68 предложений |
| Каталог | 1345 мс | 1490 типов предметов |
| Торговцы | 435 мс | 2113 лавок |
| Арбитраж | 1898 мс | 23 результата |
| Кристаллизация | 1931 мс | 3 результата; один запрос выбранного рынка |
| Дешёвые предложения | 1757–1841 мс | 389 результатов |
| Предложения по 1 адене | 1784 мс | 0 результатов |
| Крафт | 3149 мс | 45 результатов |
| Упаковки | 3146 мс | 0 результатов |
| Крафт с продажей | 3597 мс | 0 результатов |

Iron Ore, каталог и дешёвые предложения измерены на финальном 77e3d36.
Остальные времена — браузерная проверка предыдущего 0dfe790; финальный
production audit подтвердил сохранность outputs. Тяжёлые расчёты крафта/упаковок
по-прежнему занимают примерно 3–3,6 с; все endpoints не стали субсекундными.
Уведомление о возрасте presence ожидаемо при остановленном Collector.

## Причина и исправления

До исправления карточка/overview занимали 14–15 с, главные страницы профита
7–10 с. Polling безусловно abort/restart каждые 2500 мс создавал перекрывающиеся
запросы: HTTP abort не отменял выполняющийся Prisma SQL. Теперь следующий
live/activity запрос планируется после завершения предыдущего; отмена происходит
при смене scope/unmount, предыдущие данные и ошибки сохраняются.

Window-heavy offer views мешали predicate pushdown и повторяли joins/сканы.
Additive view/index migration отделила local inventory от legacy roster,
ограничила окна item partition и присоединяет выбранные лавки один раз.
Full-package доказательства, quantity/actionability и filtering сохранены.
Trader search выбирает страницу до вычисления статистики каждой лавки.

Crystallization запрашивает выбранный рынок вместо трёх скрытых дополнительных.
Начальный каталог показывает Loading во время запроса, а затем 1490 предметов.

Финальный anomaly plan сравнивал 3707 продавцов с теми же 3707, отбрасывая
13,72 млн пар. Это число сравнений, а не число предметов/предложений.
Transaction-local enable_nestloop=off, jit=off и max_parallel_workers_per_gather=0
сократили bounded EXPLAIN с 6076,862 до 1640,351 мс.
Постоянные глобальные настройки PostgreSQL и формулы anomaly не менялись.
NotificationEvaluator использует тот же исправленный сервис.

## Сохранность и проверки

- 1861 точный trader snapshot / 9692 строки, 1786 broker receipts /
  8508 переданных строк, 8885 активных stock rows и 507 доказанных omissions
  сохранены. Counts всех 21 canonical tables совпали после view/index migration.
- Полные outputs 23 arbitrage, 3 crystallization и 389 anomaly rows, включая
  fingerprints и totals, совпали. Anomaly SHA256:
  d57782f41a5a1f5a1c4a5e8598c5677330933da7b75d4b6e11b335b25bc030cd;
  potential savings 114742574, quantity 18179, revision 1980602.
- Курсоры всех четырёх рынков догнали ревизии; waiting/active queues пусты,
  current/recent delivery errors 0. Три старых failed opportunity jobs и один
  scheduled delayed job — прежнее состояние, а не новые ошибки.
- Полный predeploy и **48/48 PostgreSQL integration tests** прошли.
  Проверены seven consumer digests, filters/counts/page ordering,
  реальная production query plan и browser navigation.

Локальные browser evidence в ignored workspace: site-performance-chrome-77e3d36.json,
site-performance-chrome-0dfe790.json, site-item-fixed-20260927.png,
site-catalog-fixed-20260927.png. Cookies/tokens не экспортировались.

Collector/игра оставлены остановленными. Копии/сброс БД, покупки и ручные
уведомления не выполнялись. Исправление сайта не подтверждает полные игровые
проходы, ротацию или четыре реальные игры. Текущий статус:
[RESUME.md](../../RESUME.md).
