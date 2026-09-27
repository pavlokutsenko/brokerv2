# V38: запрос перед остановкой, сразу следующий подход

Продолжает RECHECK_PERFORMANCE_20260926.md; пользователь уточнил, что надо
быстро прочитать и сразу бежать дальше, а также показал боковую стену храма.

## Подтверждённые причины

V37 recheckplan median1.5мс, result→plan median3.25мс вместо прежних3.243с.
Но follow сначала очищал active и ждал stop1.5с, затем запускал hold_read:
близкая лавка оставалась без запроса во время ожидания остановки.
TinkMyBell был жив в63units; server origin88units и свежий0.223с при подходе.
После stop origin стал1.868с, сервер не послал новый movement packet на
zero-distance moves; policy.8с блокировала все запросы. hold закончился
server_age7.414с,0запросов. Последующий проход всё-таки прочитал лавку.

В храме V37: current83824,149058 →83913,149033, native hit side_body2,
контакт83847,149053, penetration2.212. Диагональ после входа режет боковой
столб; подтверждённые середины входов уже известны, повторного поиска нет.

## Изменения V38

- При достижении индивидуальной точки тот же reader имеет короткое окно≤.75с
  для обычного shop request ДО ожидания stop. Свежая identity/range и target
  guards сохраняются, никаких покупок/координатных записей.
- Только в этом ограниченном окне и в hold_read допускается старый origin≤8с:
  серверная позиция внутри95, локальная≤85, их XY-разница≤40,Z≤15.
  server_at не подделывается; сервер сам проверяет обычный запрос.
- Точный wire/event ответ завершает подход без Stop/sleep. Reader остаётся
  активным, ближайшая следующая цель получает обычный move. При конце всей
  операции client.close(force stop) сохраняет полную проверку остановки.
  Препятствие/неудачный ответ также сохраняют остановку и bounded replan.
- Radar detour получает свой read_goal_key: заканчивает после точного чтения,
  затем возвращается на основной путь; не проходит оставшийся хвост ради паузы.
- Внутри входа движения поперёк фасада держат центр Y известной арки до
  выхода за неё. Native sweep/границы короткого probe сохраняются. Параллельное
  движение вдоль фасада и движения вне входов не меняются.
- motion arrival_read записывает request count/seconds/captured; result.handoff
  отличает немедленную передачу движения от обычной конечной остановки.

77регрессий и build.ps1 прошли. Durable package794files установлен12:33.
V38 подтверждение: brokerfa5989ef server complete=true1676/8283.
В routee6c8517f пять early handoffs: ShadowTrader/BestGoods/TradeReal3/Ellfo
read→следующий move0.121..0.190с; D0ne имел5.174с из-за немедленного
нового radar detour и старого stop+двойной Navigation (см.V39 ниже).
Этот маршрут принял27/27exact (21sell,1buy,5package),129rows.
TinkMyBell отдельный route86fa4e30: arrival_read0.089с,2requests,
точный текущий снимок на сервере. Предыдущий connector к нему был задержан
CharacterNpc_C на площади и bounded25сек budget; следующая операция дочитала.
Center2a5bffbb завершён12:40:58. Ошибок/таймаутов wire decoder не было.

V37 перед обновлением: route3bc27351 accepted38exact, unread initially4;
routea64ac981 дочитал4/4assigned+6radar,10/10accepted,0timeouts/errors;
routee462ad97 дочитал5/5assigned+4radar,9/9accepted,0timeouts/errors.
Между пакетами сохраняется server ownership; center return9e7d6e25 завершился
до обновления. Брокер84a78039 был complete=true на сервере1682traders/8370rows.

## V39: короткие radar replans

Убирает оставшийся5.174с переход при двух новых лавках подряд: radar detour
использует один static Navigation и recheck_route с изолированной копией;
цель68units вместо дальнего конца pass tangent (>95). На radar_detour
отправляется один обычный StopIfMove без ожидания финальной неподвижности;
origin/range/native guards следующего follower сохраняются. Финальный cleanup
всегда force stop; никакого поддельного stop-cache подтверждения.
78регрессий/build прошли;794durable files установлены. Коллектор17984/game2264/
uploader11644 (перепроверять PID), enabled. Живая проверка V39 следующего
запуска выполняется. Не считать только tests/build доказательством исчерпания
рынка или исправления всех боковых стен.

## V40: читать до возврата с radar detour

Установлено 26.09 в12:57,794verified files; owner9756/game9852/uploader17048
(перепроверять).78регрессий/build.ps1 прошли. Новая диагностика arrival_read
пишет actor_present,position,distance,vertical_gap,server_age.

В V39 route0185e6e6 DealerYourHappy оказался внутри другого anchor disk:
к подходу заранее приписывался обратный хвост. Lookahead разворачивал pawn
в102units от лавки; recovery завершился у исходной точки в400units от неё.
V40 не приписывает хвост: сначала достигает endpoint68/читает, потом
существующий bounded escape выходит из чужого anchor disk и строит rejoin.
Тест требует чтение у endpoint до escape; hard cart exclusion сохраняется.

V39 первый цикл: fd73d844 принял24exact (19assigned+5radar), e2436d98 дочитал
BestGoods (.108с/2requests): суммарно20/20assigned+5radar. Center57c80af1
завершён12:47:46, следующий брокер12:48:22 был partial — без удаления.
Radar plans .0023..0033с, первые read→move .177/.191с; Gajionepegoji .081с.
MERHA затем3раза не прочитана: target_window actor_present=false в67..78units
при свежем server origin .219..222с. Это отсутствие native actor, а не
запрет старого server origin. Packet radar сам по себе не доказывает живую
лавку; проверить следующий полный брокер/поколение после рестарта.

Нужна живая проверка V40 endpoint/escape, причин пропусков и нескольких циклов.
Боковая стена храма: V38 gate centerline пока не доказан на новом реальном
пересечении входа. Повторять старое исследование3входов не требуется.

### Живая проверка V40,13:05

- Брокер49abc5e8 принят complete=true1672traders/8255rows в13:00:33.
  MERHA имела тот же OID1331725668, но новую точку82496,147975 (~598units
  от прошлой82881,147518). Старые native-absent подходы были к старой точке.
- ec38d0aa:38/38exact accepted,27assigned+11radar,218rows; next9b687eea:
  4/4accepted,3assigned+1radar,25rows; next56676fa3:2/2accepted,
  KANE69+newKane89,10rows. Итого31/31assigned+13new,44exact,253rows,
  все3формата1/3/8;0timeouts/decodererrors. Все серверные цели дочитаны,
  несмотря на первый bounded connector fail KANE69. MERHA arrival .138с.
- Реальный северный вход:13:01:49 current83701,148153→goal83783,148185;
  обычный bounded gate probe пересёк side_body;13:01:53 pawn83973,148284
  внутри храма. SindyCat12 .125с/62.6units, Oila .103с/61.8units прочитаны.
  Южный выход13:02:37 current83811,149060→83733,149060 удержал середину;
  далее pawn83674,149114 снаружи. Нет blocked events на этом проходе.
- Первые3read→move .128/.132/.185с; Sindy .574с, Oila .981с (следующий
  более сложный connector). Recheck plan median1.65мс/max.215с,
  result→plan median2.984мс. Все11new из первой операции прочитаны.
- Center6549d404 завершён13:05:40, broker39ca4f5 начал следующую работу
  сразу после штатного settling. HTTP1685active/1682checked/1pending/
  0deferred/2outside на13:06. Продолжать следующий цикл.

Оставшая причина остановки: current84043.912,148678.154 трижды получает
goal84041,148679.440, расстояние3.18. Составной проход содержит hairpin;
уменьшенный lookahead складывается в уже достигнутую точку. Обычная игра
не двигает pawn, watchdog ждёт~3с, потом пересчитывает. V41 готовится:
не посылать такой промежуточный goal<8, пробовать другой clear lookahead,
иначе сразу bounded replan/индивидуальное чтение. Финальный endpoint имеет
отдельное исключение: arrival может уже наступить между итерациями follower.

Второй V40 цикл: broker39ca4f5 accepted complete=true1669/8266 в13:06:16;
dbc2691e5assigned+Iantik=6exact accepted25rows;400ee522 дочиталMOT6+
новуюLoraKroft=2exact10rows. Все6assigned дочитаны, total8accepted.
FuMaDa новую лавку reader заметил на82559,148750; перед запросом свежий
native82513,148639 (позиция менялась), заголовки пустые.2request в65units,
timeout2с; повтор уже actor_present=false. Не покупать/не считать успешной.
Center031eaead completed13:07:58; next broker начал работу, прерван штатно
для V41. HTTP13:09:1671active/1669checked/0pending/0deferred/2outside.

## V41,13:09

81регрессия/build.ps1,795durable files установлены; owner10184/game6204
(перепроверять), collection enabled. lookahead_goal отбрасывает промежуточную
цель<8units и пробует следующий меньший clear lookahead; если их нет,
unsafe_shortcut сразу передаёт bounded replan вместо~3с watchdog. Конечный
endpoint сохраняется даже<8 (position обновляется внутри итерации follower).
Тесты проверяют folded near point, альтернативный clear goal и final arrival.
Живая проверка V41/нескольких циклов после этого изменения ещё нужна.

Offline replay workspace/replay_collapsed_lookahead.py восстановил именно
сохранённый V40 rejoin path/nav и stationary position: V41 выбрал clear goal
на8.79units вместо3.18. Последующие10integer destinations увеличивают arc
782→854→948→1043…→1580, выводят из hairpin к южному выходу без петли.
Это проверка геометрии/контроллера по файлам, не замена реальной игры.

### V41 первый настоящий цикл,13:14

Broker43f70e45 server complete=true1670/8248 в13:11:41.
Price1da5ff70:23exact105rows (16assigned+7new); последующийe09624b9:
4exact30rows (b0b4ik+Fooxy и2new). Все18/18assigned дочитаны,27accepted,
135rows, форматы1/3/8. Ни blocked/stalled/lookahead_replan, ни decoder
errors/timeouts не было в этих двух операциях. Первые5radar handoffs
read→move .128.. .140с,median.13325; arrival .072.. .241с.
Center14047ff5 completed13:14:30; HTTP1679active/1677checked/0pending/
0deferred/2outside. Следующий брокер запускается без срока/таймера.
Точное старое hairpin-место в этом маршруте не встретилось (задания западнее
храма); offline replay подтверждает контроллер, V40 подтверждает вход/выход.
