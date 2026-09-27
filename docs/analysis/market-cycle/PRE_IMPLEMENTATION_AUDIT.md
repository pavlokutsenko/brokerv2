# Single-client recurring market cycle — integration plan

Historical audit of the experimental server. Current local-cycle implementation
and production ownership: [RESUME.md](../../RESUME.md).

2026-09-25. Design only; live client and production collection unchanged.
User asked to reason through the complete cycle after the successful four-target
pass, and chose a 24-hour price recheck for otherwise unchanged shops.
Primary specification: [COLLECTION_CYCLE](../../COLLECTION_CYCLE.md).
User explicitly requires linking this cycle to broker-server. Companion contract:
`C:/broker-server/docs/COLLECTOR_CYCLE_CONTRACT.md` (design, not deployed API).
Latest user correction: retain the existing UI-assigned center per profile/city.
Every point within radius 500 is user-confirmed to see the whole market. Remove
missing traders after ONE successful full broker epoch, preserving history.
This replaces density estimation, multiple survey vantages and double absence.

## Evidence and gaps in current code

| Source | Observed behavior | Required change |
|---|---|---|
| `src/PriceCheck.Collection/Services/LocalPriceQueue.cs` | Observes radar only; `LastCheckedUtc` is saved but not used to requeue; no broker membership baseline; `PlanNext` requires live visibility | Persist verification baseline/reasons, 24h deadline and last known route destinations; late bind live actor before sending |
| Same file, `Observe` | Center membership + half-count heuristic grants authority; ObjectID changes set Pending even on first observer restart | Complete broker epoch under UI center revision drives missing-trader removal; distinguish client session remap from individual shop generation |
| `CollectionModule.PriceQueue.cs` | Moves to target coordinates with radius 8; returns to one center with radius 180 | Path planner with pass-through approaches and sampled safe center destinations |
| `CollectionModule.Broker.cs` | Timer from full pass completion; cleans up price session then runs broker; uploads result without applying a local inventory diff | Explicit phase ownership and broker reconciliation before scheduling price work |
| `CollectionModule.Refresh.cs` | Roaming radar is merged locally but uploaded only inside center | Upload positive roaming presence independently from authority to remove missing traders |
| `Services/ServerUploadOutbox.cs` | Radar emits `completePresence=true`; broker groups nonempty rows by ID, invents `Trader <id>` when unnamed; removes row multiplicity and currency from projection | Complete-epoch envelopes under configured center; quarantine unmapped IDs; retain lossless rows locally; explicit proved-empty observations |
| Same file | Price envelope lacks explicit batch ID, generation, precision, observation revision; storage is under app base | Stable snapshot IDs, precision and ordering contract; LocalAppData migration |
| `tools/BrokerWorker/src/diagnostics/collect_full_broker_inventory.py` | Fresh item catalog for types 1/3/8, raw repeated rows, request/response and truncation guards; threshold zero only | Preserve epoch/query provenance, before/after radar membership; integrate validated enchant signatures separately if required |
| `tools/WorldGeometry/walk_single_targets.py` | Four specific targets; global standoff disks suitable only for that small test | General short route horizon, opportunistic multi-shop corridors; no global standoff disks for dense whole-market queues |
| `walk_follow.py`, `walk_recovery.py` | Native checks, bounded avoidance/rejoin; repeated learned blockers last for the run | Queue-level target outcomes, timed dynamic blockers and bounded return-to-center failures |
| `walk_shops.py`, `walk_shop_wire.py` | Validated synchronized actions/cancel/move, server range gate, exact A1; per-run `done` set | Reusable owner for a profile; completion tied to job revision, not lifetime ObjectID cache |
| `C:/broker-server/apps/api/src/ingest/ingest.service.ts` | `completePresence` controls BOTH positive activation/coordinates and permission to deactivate missing | Separate positive observations from scoped negative evidence. Merely flipping the flag false loses new-trader activation |
| Same service | No monotonic observation guard; an older queued snapshot can replace current price or reactivate presence | Reject stale projection updates; preserve historical events; transactional idempotency and shop-generation checks |
| `C:/broker-server/apps/api/src/ingest/snapshot.schema.ts` | Price rows min 1; no explicit empty/precision/coverage/generation fields | Versioned contract for verified empty versus failed capture, precision and covered epoch |
| `C:/broker-server/apps/api/src/legacy/catalog.controller.ts` | Price detail returns old shop quantity as count, labels all exact rows `fresh`, not gated per trader/item by current broker inventory | Read model separating broker quantity, snapshot quantity, price age, membership and ambiguous rows; apply to aggregates/price extrema too |
| Server docs | MARKET_DATA_MODEL says qty changes enqueue; PRICE_CHECK_QUEUE says they do not and describes legacy server leases | Align documentation with user policy: local price queue, no quantity-only visits, changed composition/position/generation + 24h |

The UI center/radius is the user-assigned full-market observation zone. Authority
to reconcile missing traders additionally requires technical completion of the
whole broker epoch. The old half-count heuristic must not veto a valid complete
empty/decreased market; a failed/partial pass must never grant that authority.
Do not confuse the user's visibility guarantee with an atomic multi-minute scan:
more recent arrivals/reopens need revision-aware handling.

## Ownership and small modules

Keep existing assemblies; no new UI framework or package. Domain contracts in
PriceCheck.Contracts; policy/state in PriceCheck.Collection; driver/client
details in the worker/native adapter. No raw process pointers in policy state.

- `MarketCycleCoordinator`: one cancellable state machine per profile/PID;
  phases Recover, SelectCenter, Travel, Settle, BrokerEpoch, Reconcile,
  PriceTour, Return, Idle, Backoff, Stopped. One command owner.
- `BrokerEpochPolicy`: UI center/version, in-zone checks, expected responses,
  technical completion and atomic absent-trader reconciliation for market/city.
- `CenterDestinationPlanner`: existing UI-assigned center, frozen epoch setting,
  random free-area sampling, recent-destination avoidance, reachability.
- `InventoryReconciler`: membership/variant evidence independent of quantities;
  baseline tied to last successful price verification, explicit unknown fields.
- `PriceRefreshPolicy`: persisted reasons and revisions, daily deadlines,
  route fairness and bounded target backoff.
- `PassByRoutePlanner` + movement adapter: map/build guards, cost-aware short
  routes, smooth entry/pass/exit, recovery and return estimates.
- `ShopReadCoordinator`: validated ordinary actions, server cancel and wire
  precision, asynchronous bounded replies, revision-safe completion.
- Existing outbox: durable capture-before-complete and asynchronous upload.
  Broker/price phase transitions drain outstanding work before hook handoff.

Catalog identity: market/city/traderKey. Normalize consistently across C#,
Python and server; define and test Unicode/whitespace migration before changing
existing uppercase keys. Actor/ObjectID bindings include client session ID.
Relog does not reset durable price times or prove all shops reopened.

Required local record groups:

| Record | Minimal facts |
|---|---|
| Trader presence | Durable key, last positive time/position, visibility state, evidence of closure, observation revision |
| Runtime binding | PID/client session, current ObjectID, validated actor token; never reused across sessions |
| Broker observation | Epoch, query/side/item/variant knowledge, raw multiplicity, aggregate quantity, interval, coverage/completeness |
| Verification baseline | Snapshot ID, capturedAt, precision, shop generation, verified position and composition |
| Queue job | Set of reasons with revisions, dueAt, nextEligibleAt, attempts/error, last attempt, scheduling age |
| Cycle checkpoint | Phase, configured center/revision and point history, unfinished jobs, next broker deadline; actor pointers excluded |

Unknown enchant is not +0. Quantities do not participate in the price-change
trigger. New row multiplicity is a conservative trigger, but same-count
replacement may remain invisible; the 24h policy explicitly bounds scheduled
rechecks, not immediate detection. Price-only changes are also not broker facts.
Keep runtime/database quantities and prices as int64/BigInt; JSON wire integers
must survive JS parsing (decimal strings where needed), including values >2^53.

## Defaults to validate, not established protocol constants

User-defined: UI-assigned center, full visibility anywhere in its radius 500,
one complete broker epoch removes missing traders, unchanged-shop recheck after 24h.
Keep profile's broker interval (currently default 5min after pass completion).
Research-validated: pass arc ~72, planning standoff 55, execution disk 45,
read radius 85 (max 95), observed server position age <=0.8s, pending reply
timeout 2s, one action pair per 0.6s, ordinary action/action/cancel/move order.
Initial policy choices: center settle >=3s; destination separation ~100;
position change threshold 20 XY with repeat confirmation; two local recoveries
/ ~12s per target; 2/5/15min backoff. Tune these defaults using logs; preserve
the user-assigned center instead of adding automatic density/coverage discovery.

## Implementation order and acceptance

1. Pure policy models and replay tests: catalog, membership, revisions, deadlines,
   epoch completeness, fairness and checkpoint persistence. Migrate app-base state to
   LocalAppData once, preserving last checks/backoff/outbox IDs; one writer.
2. Server read/ingest contract: explicit positive presence, bounded negative
   scope, quantity/price separation, verified-empty and precision, monotonic
   observations. Keep backward-compatible transport until collector switches.
3. Integrate center planner and broker diff into the coordinator. Replay saved
   market observations and compare dry-run decisions against current behavior.
4. Package the validated pass-by implementation behind a worker command; no
   production dependency on research JSON or the old collector. Keep old route
   selectable for rollback; do not run two owners on one PID.
5. Bounded live tests: center choices from different directions, one complete
   broker phase plus changed-target route, then at least three cycles. Verify
   target cancellation, quantities/prices on the site and cleanup. Only after
   this make recurring mode the active route. Daily behavior tested with a fake
   clock; no need to run the live client for 24h to test scheduling.

Required policy/contract cases:

- Quantity-only decrease/increase: new broker amount, no new price job.
- New item/variant/extra row: one pending job; failed attempt survives later
  identical broker passes; later mutation cannot be cleared by an earlier reply.
- One full in-zone epoch omits a trader: remove from active market and route
  queue immediately; return later creates a fresh job. Keep history. Full valid
  zero and >50% shrink work; timeout/truncation/partial pass never delete missing.
- Same item/enchant at two prices: aggregate stock not copied into both offers.
  Sold-out broker membership cannot still contribute stale price minima.
- 23h59m versus 24h, app restart and upload retry: deadline remains tied to
  successful capture, not broker observation, attempt, arrival or ingestion time.
- Move jitter versus accumulated actual relocation; changed floor and shop type.
- Roaming knownlist loss versus confirmed closure; cold radar and ring overflow;
  a new trader discovered outside center activates positively without deleting others.
- New client session/ID remap versus genuine individual shop generation change.
- UI center absent or reassigned mid-epoch, random point behind stair partition,
  unreachable point, radius boundary, wrong city/map and out-of-zone interruption.
- Trader leaves while approaching; request/reply races a re-open; delayed packet
  arrives after confirmed closure; unsupported buy/package precision.
- NPC block expires; static wall remains; recovery skips one target and keeps
  progress; center return failure stops boundedly; F8/user intervention cleans up.
- Huge queue or constant arrivals: broker refresh and old/daily targets all
  make progress; empty queue waits; no repeated hook install per trader.
- Network unavailable, duplicate or out-of-order upload, crash before and after
  outbox persistence: no extra visits or resurrection of obsolete current data.

Success requires logs showing why each trader was visited/skipped/deferred,
independent ages for broker stock and exact price, the configured center/epoch,
and a route that never targets the trader's coordinates as its destination.
