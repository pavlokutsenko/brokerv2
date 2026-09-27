# Existing deployed Market integration — 2026-09-27

## Final status

Existing site deployed at `https://pog-sandbox.com`, API base
`https://pog-sandbox.com/api`, release `f5bb028`. Installed collector V68 build
`5a2f07b3194e4f5a8bb89283c9f92491`; all four profiles point to that API.
1861 exact trader snapshots /9692 rows and1786 independent broker operations
/8508 stock rows verified on production. Quantitychanges1834 and507 proven
item omissions applied; price data unchanged, no extra price requirements.
All outboxes empty; settings/history preserved; no database copies made.
Final audit36302427247: stock/price mismatches0; notification pending revisions0,
waiting/active0; no current queue/delivery/projection errors. Four real Telegram
messages succeeded during initial price cutover; no new delivery after stock
transfer, whose revision was fully evaluated. No synthetic live test messages.
Collector/game stopped. Full game acceptance is still a separate next phase;
this is not a claim that every original live-cycle scenario is complete.


The user requested adaptation and deployment of the existing full website in
`C:\PriceCheck\market`, then routing new collector data there. Collector/game
remain stopped until this integration is verified; live collection tests come
afterward. The existing site is `https://pog-sandbox.com`; collector API base is
`https://pog-sandbox.com/api` (Caddy removes `/api`).

## Ownership and preservation

- The old full Market owns production PostgreSQL, calculations, Telegram,
  subscriptions and worker state. Do not replace it with `C:\broker-server`:
  that experimental API does not implement the complete business pipeline.
- Add `/ingest/local-cycle/{price,state,broker,history}` to the deployed Market.
  Existing ingest and other consumers remain compatible outside adopted scopes.
- Separate broker quantity from exact price; all exact int64 values remain
  decimal strings on the wire. One shop is one durable operation.
- Explicit close only removes a shop with fresh observer proof inside the
  unchanged trusted Giran center radius 500. Outside close/reopen invalidates
  verification without removing last-known listings. No disappearance by broker
  absence, visibility loss, or age in adopted scopes.
- Use existing business calculations and notification worker; new changes must
  advance their market revision/watermark. Stale/ambiguous offers remain visible
  but cannot produce actionable profit alerts.

## No backups

The user explicitly instructed **no copies/backups** during this task. No
production database archive/clone/restore copy was created. The old operations
documentation incorrectly promised a six-hour backup job; it has been corrected.
Migrations are additive and tested on a separate synthetic local database.
Production preservation is checked with read-only counts before/after migration.

## Collector switch

`scripts/retarget-collector-server.ps1` defaults to dry-run. Applying requires a
stopped collector, destination readiness and the local-cycle history endpoint.
It updates profile ServerUrl and existing SQLite outbox destinations without
changing operation IDs/payloads, history, centers, credentials or rotation. It
does not create database copies. An already existing configuration recovery
file is kept consistent. Nonempty legacy ready outbox causes a deliberate stop
because its payload contract needs separate translation.

Preflight: four configured profiles, three market databases, no pending outbox
operations. Local snapshot history contains 458 exact reads (366 sell, 51 buy,
41 package). Previously delivered snapshots are not blindly replayed as current
prices: later state events are not retained in the old local receipt table, and
such replay could resurrect a subsequently closed/reopened shop.

Five isolated migration-helper tests passed. ModuleIsolation smoke passed.
V68 staging/build is separate from installed V67; deployment/switch are pending.
Production readiness currently passes; no collector or game process is running.

## Deployment checkpoint — 09:32 Europe/Kiev

Market commit `e12ea23` pushed to main; ordinary CI/deploy is running. Complete
predeploy gate passed (719 API tests, 151 web tests, additional catalog/forum
tests, audits, typecheck/build). Fresh migrations passed; final PostgreSQL suite
45/45 and deterministic ingest harness passed. Two test-isolation issues were
fixed: shared logical maintenance clock, and legacy query scoped to its market.

Real capture replay on the separate local test DB accepted 458 snapshots/2210
rows, with 447 latest traders/2159 rows. Full repeat produced 458 duplicates.
Source SQLite was read-only and streamed in memory, no database/payload copy.

Switch preflight found the local history also contains prices adopted from the
experimental local server. Merely changing URL would leave those locally fresh
shops absent from production until recheck. Prepare a bounded transfer of
currently valid exact snapshots from local PostgreSQL, preserving original IDs,
capture time and int64 values, through durable per-trader SQLite outbox.
Read-only candidate audit: 1861 valid in-boundary current exact snapshots;
22 additional snapshots excluded because the shop changed after their read; no outside-boundary candidates passed the preceding validity filters. Of 1753 clean fresh local
shops, 1752 are covered. One (`LLOOKATMEL`) has a historically acknowledged read
that the server rejected as current due to a generation mismatch; its check
state must be corrected without rewriting LastRead/history. Do not promote this
old result or invent a fresh closure proof.

Collector agent is fixing obsolete-result ACK/reconciliation handling with
tests. Cutover agent is preparing a dry-run-first transfer helper. Root created
`tools/LocalOutboxSender`, which drains ordinary durable operations with no
reader/game launch. Do not apply transfer/destination changes until production
readiness and new contract are verified. No backup/copy requested or created.

## Work in progress

Market agents own additive ingestion, business read models/tests, and deployment
checks/documentation. Root owns collector endpoint migration/build and release
coordination. Dedicated test database:
`postgresql://pricecheck@127.0.0.1:5432/pricecheck_site_integration_20260927`.
No production reset, truncation, broker purchase, or old-collector launch.

## Collector installed — 09:43 Europe/Kiev

Final V68 stage `workspace\market-site-v68-final-stage-20260927-0639` was
published to `release\PriceCheckCollector` with durable verification of all
827 files. Build ID `5a2f07b3194e4f5a8bb89283c9f92491`.
Collector SHA256 `B7D7044F3CE13CB65AA0C446D54FC6CF3D38C2CEECE814F352FC0F4D89432626`;
Collection SHA256 `881C9118D7CC6CC37DD253468559D7F6922B49F8D668EE41D86C1B3553C5CFCC`.
All 354 Broker/ClientLaunch/DriverRuntime files unchanged. No collector/game
started. New acknowledgement/reconciliation regression suites passed (70
LocalCycle checks), ModuleIsolation and standalone outbox sender compiled.

Current broker audit, prompted by the user: verify quantity-only updates do
not reread prices, new positions enter the local route, complete stock removal
changes every profit calculation, partial stock preserves prior values, and
notification revision advances without requiring a new exact read. Transfer
is held until the production baseline and this audit are complete.

## Cutover applied — 09:48 Europe/Kiev

Production e12 deployment and readiness passed; all 21 canonical pre/post
migration table counts matched (see diagnostics/market-release-baseline-20260927.md).
Safe transfer helper atomically enqueued 1861 current exact snapshots / 9692 rows.
434 original SQLite payloads were preserved; 22 shop-changed-after-read results
excluded, outside count zero. LLOOKATMEL check made dirty using factual server
revision, original successful read time and snapshot history retained.
All four profiles now use `https://pog-sandbox.com/api`; three local databases
preserved. Configuration hash after removing only ServerUrl matched exactly:
`959CDB8F12A5D69F1268F48C05B4E3C0E013C65C5B0F686A195E6A8703CBA923`.
Standalone LocalOutboxSender is delivering individual payloads without a game
reader; delivery completion and remote exact-row audit still pending.
Production duplicate probe `route-1f43d20921724c679b14df6493abe4fb-1306564439-48`
returned accepted=true, duplicate=true, current=true, rowCount=9.

Broker targeted regressions passed locally and on separate PostgreSQL:
quantity-only full/partial changes and complete item removal do not invalidate
price; new item/additional physical listing schedules a next-segment target;
newer/equal-epoch reads are protected. Native/runtime code unchanged by this
audit. Existing site profit services use actionable prices without forcing a
fresh exact read every ten minutes. A follow-up server patch will wake the
notification queue immediately after commit, retaining the existing 15-second
polling fallback if Redis is unavailable.

## Delivery completed 09:53; broker follow-up 09:54 Europe/Kiev

Standalone sender exited successfully: 1861 individually accepted traders,
zero warnings/retries; Gamma/Black/White outboxes empty. SQLite quick_check=ok;
1933 known traders, 458 immutable local snapshots and 1821 last-read timestamps
remain. No game/collector process was started. Post-delivery server exact-row
comparison is running via read-only diagnostics, no database copy.

Follow-up Market commit `f5bb028` pushed to main: notification queue wake after
successful local-cycle commit, safe fixed/rate-limited warning on Redis failure,
no wake on duplicates/ignored/historical-only reads. Existing poll fallback
remains. Full predeploy gate passed (722 API unit tests, 151 web tests and other
existing gates); full PostgreSQL integration 47/47 passed. A concurrent fixture
race in global maintenance tests was corrected without changing runtime cleanup.
Regression uses 25-hour-old valid exact prices and actual broker service calls:
quantity changes alter all four profit domains, complete omissions remove item
opportunities, partial omissions retain them, raw exact history stays unchanged,
all traders remain active, and mock Telegram delivery/dedup state is checked.
No real test messages were sent. This follow-up deployment is in progress.

## Production exact-data verification — 09:56 Europe/Kiev

Read-only audit run 36301466832 succeeded: price receipts 1861; current exact
traders 1861; expected/stored rows 9692/9692; row mismatches 0; metadata mismatches
0. Checked price, quantity, buyCount, basePrice, item/itemObject, enchant,
rowIndex, package/type, snapshot identity, coordinates and original read times.
Receipt fingerprint `2901fbb9c836dcc9d157ea9a9fbba9f4`.
Gamma's 2681 retained traders include legacy state; visible 2113 shops/10856
rows, actionable exact rows 9692. Existing records were not reset.
Projection pending/errors zero; notification cursor processed the new revision,
no current evaluation/delivery failures. Delivery completed count grew from
zero before cutover to four; these are actual Telegram delivery worker jobs
(success requires Telegram HTTP and payload ok), not mocked integration jobs.
Completion timestamp audit 36301633023 proved all four after cutover: first 06:52:09.643 UTC, last 06:55:07.909 UTC. No manual test messages.

## Additional broker stock cutover audit

User explicitly emphasized quantity-driven profit without redundant price reads.
Price transfer alone preserves exact read quantities but some later broker stock
is newer: 1786 imported traders have later broker observations, 8984 newer stock
rows (8508 active / 476 inactive), 1834 active quantities differ. No newly added
item/side or higher listingCount found among these candidates. Retained native
broker run files are being checked for original epoch times and complete
composition proof before a separate durable per-trader stock transfer. Do not
manufacture completeness from a PostgreSQL stock table alone; legacy receipt
hashes do not preserve the original operation body or full epoch metadata.

Follow-up deployment `f5bb02821c75c84a02d7462f7cfc9dae1dd03753` succeeded:
https://github.com/pavlokutsenko/pricecheck-market/actions/runs/36301496334.
Readiness passes. Notification wake-on-commit is live. No new migration/reset.
Pre-broker data baseline is being rechecked after this release.

## Broker stock transfer started

`scripts/migrate-local-broker-to-site.ps1 -Execute` enqueued 1786 individual
broker operations / 8508 active aggregate rows. 1834 quantities differ from
exact-read counts; 507 omissions are proven complete (0 newer-read protected
omissions). 1678 reconstructed original operations match original receipt
SHA256; 108 older operations are proven by completed batches/source/start and
matching raw identity files. All 8 epochs represented, no partial fallback or
unproven/excluded payloads; no new price targets introduced.
`tests/BrokerCutover.Smoke.ps1` passed actual rollback PostgreSQL replay of 11
representatives/all epochs, buy/package/removal, no price requirements, original
exact rows unchanged, duplicate and newer-read guards. SQLite durable kind/ID/
payload/replay tests and prior PriceCutover smoke passed. Native files remain
unchanged. Standalone sender is delivering; final broker receipt/projection
comparison still pending. No database copies or game launch.

Broker sender completed with exit 0: all 1786 operations delivered, no retry
warnings, all three market outboxes empty. Actual HTTP broker replay returned
accepted=true/duplicate=true. Final source audit: SQLite quick_check=ok;
1933 traders / 458 immutable snapshots / 1821 lastRead values retained; all
four profile URLs correct, other settings hash unchanged, enabled profiles 0,
collector/native worker/game processes 0. Installed build remains V68
5a2f07b3194e4f5a8bb89283c9f92491. Final server audit run 36302304018 pending.

## Final broker data audit

Read-only run 36302304018 succeeded: broker receipts/latest applied 1786/1786,
all complete and proven; expected active items8508, unknown quantities0,
stock mismatches0. Eligible complete omissions507, active omission violations0.
Active stock9392 ->8885 exactly matches507 removed aggregate positions.
Current exact traders1861; raw exact rows9692/9692; metadata/row mismatches0;
price receipt fingerprint unchanged; required prices0. Visible catalog10341
rows, actionable9138 after quantity/disappearance and ambiguity safeguards.
No shop was removed by broker. Notifications were still finishing194 pending
revisions in the sampled audit (waiting1/active1, no errors); a bounded final
read-only check is pending. Do not mistake a queued recalculation for failure
or claim cursor catch-up before observing it.
