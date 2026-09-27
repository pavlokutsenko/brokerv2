# V67: local Giran collection cycle

This is the active design for the 2026-09-26 rewrite. It supersedes server
assignments, broker absence removal, retained routes across character changes,
and broker completeness retry rules in the older checkpoints.

## Non-negotiable state rules

- The only city is Giran. Use the saved profile center unchanged. The existing
  collection boundary, temple/entrances/walls and cart exclusion remain intact.
- Close/reopen invalidates price everywhere, even if items/counts are identical.
  Repeated open packets and a new client/ObjectID do not imply reopening.
- Only a fresh explicit closed observation made from within 500 of the saved
  center authorizes removal of offers. A move into center cannot refresh an old
  outside observation. Knownlist deletion, missing actors and broker absence
  never authorize removal. From center the market is visible; no per-trader
  approach is required to confirm the current state.
- Exact reads and quantities are separate. One shop becomes one durable exact
  snapshot/outbox operation immediately, without waiting for HTTP or other shops.
  A newer invalidation cannot be cleared by an older in-flight read/delivery.
- A partial broker is WARNING and preserves known data. After safe native cleanup
  continue local prices; do not wait/retry the broker in place. Native command
  cleanup failure is a client-recovery fault, not ordinary incompleteness.

## Storage and exchange

Local market SQLite under LocalAppData uses WAL, synchronous FULL and short
transactions. Durable key is normalized market + Giran + traderKey. It keeps
known shops, last observation/read, required revision/reason, finite route,
failures, immutable successful snapshots and outbox separately. Snapshot and
outbox insertion are one transaction; delivery retries retain snapshot/event IDs.
Current-process action bindings stay session scoped.

Server adds `/ingest/local-cycle/{price,broker,state,history}` without changing
legacy consumers. It stores exact prices independently of broker admission,
quantity and price timestamps separately, idempotent receipts and monotonic state.
The local collector does not request leases/targets/retries from the server.
Initial history reconciliation is batched and preserves pending local readings.

A new client attaches the radar before login, waits for the actual world, goes
to the saved center, attempts broker, reconciles history and builds a fresh route.
History/outbox remain; old route/counters/ObjectIDs do not. Natural cycle:
reachable random center point within 500 → broker → finite locally planned pass
→ center → immediate broker. Old unchanged shops are due after a configurable
interval, default 24 hours, measured from the real successful read.

## Ownership during implementation

- root: integration, Radar runtime, Python broker/native route interface,
  native guard preservation, installed deployment and live verification.
- local_cycle (Sol): Collection application, contracts/local store/outbox/route.
- server (Sol): broker-server endpoints/migrations/tests/server installation.
- ui_install (Sol): WPF/launch/config/logs and Windows packaging/service host.

No existing repository changes have been discarded. No market database reset.
Old offline research export is untouched. No purchases. Never attach two readers
to one game process. V65 ProcessEvent parameter-buffer lifetime guard remains.

## Evidence to date

- Initial process inspection: collector and game were stopped; previous V65 release
  remains distinct from uninstalled V66 stage. No API/Postgres listeners initially.
- Saved Gamma center: (82413.61851503256, 148116.9785946493), unchanged.
  Saved primary profile 6a0358a7-984d-4ef1-8570-283b79c3cf88. Rotation is restored enabled10±2. Proxy false.
- Server additive migration applied after durable pg_dump backup, existing row
  counts preserved: 2184 traders / 2439 snapshots / 12105 exact rows / 11066 stocks.
- `RadarLifecycle.Smoke` passed: early acquisition while collection stopped,
  rapid close/reopen between UI ticks, unchanged open suppression, visibility loss,
  old closure/observer-position separation and fresh client identity.
- 34 BrokerWorker tests passed after partial-response validation/no identity retry.
- 122 WorldGeometry tests passed, including V65 running-command buffer retention
  and isolation of the detailed local section from future dynamic targets.
- 42 local SQLite/protocol/recovery/tail checks passed, including actual forced
  child-process termination and partial UTF-8 journal records.
- Server build and isolated PostgreSQL protocol tests reported passed by server agent.

## Live evidence and current installation

V67 was installed and started at 20:08:11 UTC (owner20564). First build
470f0915047d4b7f86ce4f2b49cccb09 is superseded by installed build
ddccaeecb9fd4323aa650b8ee1221107, published at20:25:51 UTC,827 verified files.
Owner10596 started20:26:06 UTC with `--resume-character` after a normal shutdown.
Always rediscover PIDs; these are evidence, not process bindings.

- Real broker passes:1909/1909 replies1755traders, then1900/1900 replies1764traders.
  Zero unbound names; identity cache recovered1 and2 actors absent from final native
  scan respectively. This is positive evidence, not proof every timing race is gone.
- Server/native audit:77individual exact shops357rows, alltypes1/3/8,zero mismatches
  in price/int64quantity/enchant/package/item identity/coordinates. Normal delivery
  was38–159ms in the initial sample, before the current route section ended.
- Actual center closure GANCHOZO was sent separately before broker completion;
  fresh reopening invalidated the price. Outside-center DANILAKOLBASENKO and
  AMEJIUA emitted pending closure without removal. AMEJIUA reopened and was reread:
  item4725 qty1 enchant3, price33,333,333→32,999,999. The previous read was three
  hours earlier; the exact instant of the price edit was not observed.
- API outage20:15:28.176→20:15:46.840 UTC: movement/radar/readings continued;
  UPYPY10rows and PIPISKINJAZZ3rows remained durable and retried with identical IDs.
  Both arrived automatically, including the older result23seconds after capture.
- Natural rotation0→1 of the observed seven-character roster created new PID19164,
  early reader, center/broker/history and new route. First66shops and next11shops
  had zero trader overlap: a new client did not force fresh shops to be reread.
- Updating retained all77snapshots and1520successful-read timestamps; outbox0.
  Four accidental fixtures from an old test harness were removed only after exact
  timestamp/coordinate/empty-history guards and SQLite/WAL backups. Production
  server rejected all fixture payloads as outside the boundary. The harness now uses only temporary synthetic markets; no market database was reset.
- Live review found the old native revisit loop extending20-target sections with
  future targets. Current build restricts off-route approaches to the assigned
  section; nearby on-path reads remain allowed. This correction still needs a
  complete live pass. Native journal consumption is incremental, with read-start,
  duration, precise failure and successful durable-result events in the UI journal.

Do not infer readiness from builds/tests or two broker passes. Remaining live
acceptance includes complete passes, center→immediate next broker and several
cycles with the corrected section execution, plus owned crash/disconnect recovery.
Partial broker continuation has automated coverage but has not yet occurred in
this live run. One configured Gamma game is available; four real markets and
native login into other servers are unverified. See docs/INSTALLATION.md for
isolated installer/service recovery evidence and the unavailable clean-VM check.

## Validation update at20:41 UTC

Stage ac5482a4fb434d9a8b0a817f47743aec (827files,20:39:21Z) adds the client
outside-boundary closure filter, cancelled-section handling and explicit broker
start/center-arrival journal records. It is not yet the installed ddcc build.
The new isolated ModuleIsolation suite passed four synthetic market profiles,
PID ownership, individual durable results, new-client history, incomplete broker
continuation, and unsafe native cleanup recovery. It does not touch real market DBs.

Installed ddcc completed its first three bounded sections20/20 assigned each.
Natural rotation1→2 stopped section4 safely; new reader19096 attached20:36:07Z,
before entering world. Broker1913/1913,1776traders finished20:39:04Z. New pass
had299initial targets compared with334previously; its first40snapshots had zero
trader overlap with the previous clients' fresh successful reads. Old route was
cancelled; local history and outbox survived. Full finite pass is still pending.

Frozen server/native audit20:36:31Z:151individual snapshots733exactrows, no field
mismatches and no pending delivery. Server current-projection audit found zero
current exact snapshots for traders whose reopened/pending/confirmed/moved/type/
new-listings revision remained unverified. The API also rejects a removal proof
unless kioskType is explicitly0, even when its center coordinates are valid.

## Actual native fault and recovery20:44–20:47 UTC

At20:44:03Z PID19096 did not consume a pause-for-plan ProcessEvent; cleanup
reported bridge busy(trigger1/status0). Existing V65 lifetime guards retained
uncertain command buffers. The collector selected owned-client recovery rather
than treating this as an incomplete broker. Replacement PID9452 connected its
reader20:44:28Z before login and resumed the same character2 at20:44:43Z.

Paired local/server audit retained239immutable snapshots1087exactrows with zero
mismatches and localoutbox0.1651 successful-read timestamps survived. No failed
native operation was marked successfully read. The old route was cleared.
At20:47:25Z the replacement completed broker1910/1910,1781traders, created a
fresh239-target route and delivered new per-trader readings from20:47:38Z.
This is actual recovery evidence; it does not establish the cause of the game
thread stall or prove its elimination. A diagnostic-only finally-path issue
(marking unattempted targets as shop failures on fatal infrastructure errors)
is being corrected before the next installation.
