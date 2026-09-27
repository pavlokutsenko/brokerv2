# Market cycle implementation and evidence

2026-09-25. Active implementation in C:/broker and C:/broker-server.
Follow-up: distributed server scheduling and power-loss recovery supersede the
local queue described below. See DISTRIBUTED_QUEUE.md for current ownership,
tests and deployment; CycleQueue is no longer used by the active coordinator.
User policy: manual UI center/radius 500, broker-only admission, one complete
epoch removes absent traders, quantity-only changes never require a visit,
unchanged shops rechecked after 24 hours, independent profiles/PIDs/markets.

## Code map

- Contracts/Models/MarketCycle.cs: durable queue state and UI status.
- Collection/Services/CycleQueue*.cs: admission, revisions, daily expiry,
  quantity-independent composition and retry deadlines.
- Collection/CollectionModule.Cycle*.cs: one worker per profile, center/broker/
  price/idle phases, graceful stop, frozen upload scope and durable results.
- WorldGeometry/cycle_plan.py and cycle_route.py: packaged reachable-center
  selection, spaced flyby anchors, native obstacle checks, bounded recovery.
- BrokerWorker/client/session_cache.py: cache reuse only after current PID,
  module, connection, socket, key and wrapper validation.
- Collection/Services/ServerUploadOutbox.Cycle.cs: version 2 delivery.
- Collector/CollectionPanelView.xaml: WPF settings, phase/counters, queue,
  map/events, last/next broker pass and delivery status; no new UI framework.
- broker-server/apps/api/src/ingest/cycle-*.ts: ordered, idempotent ingestion.
- broker-server/apps/api/src/legacy/market-offers.ts: broker stock with exact
  price history and explicit ambiguous per-lot remainder.

## Verification

| Check | Result |
|---|---|
| build.ps1, packaged BrokerWorker and self-contained desktop | Passed |
| MarketCycle.Smoke | 16 policy scenarios passed |
| ModuleIsolation.Smoke | Lifecycle, two simultaneous simulated market cycles, scoped stop and exact outbox passed |
| DriverConcurrency.Smoke | Real driver, 2 synthetic PIDs, 8 handles, 4,000 reads, no cross-PID data |
| WorldGeometry tests | 28 passed, including all-anchor standoff and reachable center |
| BrokerWorker tests | 11 passed, including stale cache/changed session guards |
| Exact wire decoder | 2 passed |
| API integration | Dedicated pricecheck_cycle_test_20260925 DB; idempotency, BigInt > 2^53, partial/full absence, ordering, legacy isolation, market isolation and ambiguous lot quantities passed |
| Web component tests | 5 passed; web/API build passed |
| WPF visual check | Actual view rendered to workspace/market-cycle-ui.png; settings scroll at short height |

Integration fixtures derive a new observation interval on each run; they require
a dedicated pricecheck_cycle_test_* database and never target production.
Run from C:/broker-server/apps/api with its tsconfig: node_modules/.bin/tsx.cmd
--test scripts/market-cycle.test.ts, with DATABASE_URL pointing to that test DB.

## Bounded live control, PID 19308

Evidence in C:/broker/workspace/cycle-live; no indefinite loop left running.

- Center from the user's saved profile (82413.6185,148116.9786), radius 500.
  Packaged return completed in 11.125 s, 1,140 traveled units, 26 native checks,
  no recoveries; destination within the configured circle, stop drift zero.
- Full protocol broker capture: 35.037 s, 1,718 traders / 8,997 rows. Fresh actor
  binding resolved 1,706. Unknown bindings intentionally make market authority
  partial: positive records are usable; **this capture cannot delete absences**.
- Packaged price tour: 41.2 s, 24 ordinary sell shops / 142 exact int64 rows,
  49 actions, 25 server cancels and replies, no invalid replies, no recoveries,
  final stop drift zero. One unanswered attempt and two range skips were bounded.
- Independent restoration read: selected target zero, original ProcessEvent,
  send/post entry points and approach slot restored. Existing radar retained.
- Offline export: 26 envelopes in workspace/cycle-live/review-outbox (partial
  broker, 24 exact shop snapshots, stopped status). Control delivery to local API
  was blocked by automatic approval review, reason only "blocked by policy".
  It has NOT been sent; user approval question remains pending.

Server migration 20260925120000_market_cycle_v2 was applied to the local database.
Backup: C:/broker-server/.runtime/backups/before-market-cycle-20260925.dump.
Local API health and web preview returned success. Integration fixtures ran in
the separate test database; these are not evidence of uploaded live captures.

## Remaining validation and limitations

- Only one game is open: two real clients moving simultaneously and a long
  unattended soak remain untested. Driver concurrency and coordinator isolation
  are separate passing checks, not a substitute for that live test.
- Validated navigation is Giran/current build only. Exact wire prices are proven
  only for ordinary sell A1; buy/package layouts remain unverified/deferred.
- Same item/count replacement or price-only change is caught on the daily read;
  full-market EnchantMin is not part of this loop.
- No API queue hydration or automatic import of the legacy price queue. Local
  state loss causes a fresh price pass. Existing research/state files preserved.
- Native receiver/sender retirement preserves potentially referenced buffers
  until game exit (~1 MiB per price tour). A long-running reuse/reclamation design
  is still needed; indefinite resource stability is not established.
- Build/layout/RVA guards remain necessary; driver use does not make arbitrary
  client updates compatible. See DRIVER_COMPATIBILITY.md.

New executable: C:/broker/release/PriceCheckCollector/PriceCheck.Collector.exe.
The old desktop was not closed automatically: its launcher owns the open game
and closing it may terminate that game. Do not run two collectors on the same PID.
