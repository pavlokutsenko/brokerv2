# Background route preparation and one temple section — 2026-09-27

User requested shorter stops, background route calculation, a single temple
visit, new-shop priority checks and a real1 Adena test submission.

Observed old0444 session had2.42–2.75s gaps during ordinary recoverable
replanning: full1.5s stop settlement plus0.9s fixed drift sampling. Separate
section preparation took6–7s. At18:21 UTC it had335 exact captures/1509 rows,
325 current ACKs and10 historical receipts, no pending receipts/outbox. This
was not a completed pass.

The host now starts one `market-plan` subprocess while its current route runs.
The planner imports only offline geometry/policies and never opens a game client.
It prepares the next section from the predicted endpoint, including immutable
map/grid geometry. Only one speculative task exists per cycle; bounded60s,
shared stop file and session-generation checks cancel it. Two fixed LocalAppData
files are overwritten atomically, avoiding accumulating temporary map copies.

Execution checks map hash, actual capsule clearance, complete target membership,
positions, identity and verification generations. It reconnects from the actual
current position only with clear conservative geometry. A changed target list
discards the path but can reuse identical map geometry. Missing, corrupt, stale
or unusable output falls back to foreground planning. Every movement still uses
the ordinary runtime capsule/world/target/native guards.

Temporary recovery pauses now repeat ordinary StopIfMove and observe180ms rest;
unsettled movement falls back to the existing strict stop. Final close/cancel
retains full1.5s settling and ownership cleanup. Individual approaches use this
brief planning pause, avoiding a redundant strict stop before each replan. This
does not promise zero stops: collision recovery, exact replies and final section
cleanup remain bounded reasons to stop.

Temple shops form one route cluster and one section instead of separate20-shop
batches. The section uses the surveyed `Giran_CH_in_front` floor/inner-wall
envelope X83881..84541/Y147980..149260; it does not expand the market boundary.
Geometry evidence is `maps/Giran/giran-navigation-2026-09-26-expanded.json` and
the prior native gate validation in `analysis/market-cycle/TEMPLE_20260926.md`.
Ordinary outdoor sections stay20. A65-shop temple fixture stays in one section;
the next background section excludes every current temple target. Fresh later
openings and failed/unreachable reads can still require a later visit, so actual
gate crossings must be verified rather than claiming a guaranteed one-entry run.

Priority fixture proves a newly observed shop ahead is inserted before existing
nearby work, while a shop behind goes to the end to avoid reversing. Finite
admission remains unchanged. This is forward-section priority, not global
immediate preemption of an already moving worker.

Real HTTP acceptance in the isolated local PostgreSQL integration database:
`C:/PriceCheck/market/apps/api/src/integration/one-adena-http.integration.ts`.
POST ingest price strings with price1, followed by GET1 Adena deals/arbitrage:
weapon/armor/accessory appear exactly once; Buy1/package/resource/price2 do not,
price0 rejected400. Three seller units at1 and two buyer units at101 produce
volume2/capital2/revenue202/profit200. Broker quantity1 changes profit to100;
moved generation invalidates, old generation cannot restore, fresh generation
restores; empty complete broker removes the row and confirmed close removes it.
Fixtures are deleted afterward. No synthetic traders/prices were sent to
production. API typecheck passes; no production runtime change was required.

Source validation: LocalCycle smoke including65-shop room/new-shop ordering
passes;73 navigation/geometry/revisit/continuation/performance tests pass, then
the additional corrupt-cache fallback test passes. Packaged offline planner
successfully calculates a real9-anchor435-point section in5.44s using PID0,
confirming it needs no live client. Final74 checks pass; rootbuild produced
433162c1c7494ff9878cee70a8cf2dd3 and durable publication verified841 files.
Collector3568/Gamma20516 launched18:25:40/18:25:44 UTC, collection18:26:14.
Prior22892/13208 closed by the agent for this authorized update. Own build stage
removed after publication. New center/cached-plan/live temple acceptance pending.

Offline acceptance on all56 supported temple shops from the real complete
17:58 native snapshot: route planning4.96s,13 anchors,360 points, no deferred
anchors. The main path crosses the room boundary exactly once to enter, never
exits and ends inside.52/56 positions come within95 on the main path; the other4
require the usual individual revisits inside the same section. This proves the
planned geometry on real coordinates, not actual price capture/gate crossings.
Evidence is private `temple-offline-acceptance.json` in the watch directory.

Live433162 acceptance: centre18:28:48 complete2073 identities,1790 supported
inside the zone. Every1790 identity is present active in the whole server roster;
17 extra in-zone opens all have newer timestamps.53 absent retirements accepted;
51 remain inactive, M19 and TIMEZONE have real later positive observations (M19
later closed_pending outside centre), so they must not be forcibly retired again.
68 exact captures/311rows67currentACK+1historical receipt, queue0.

Live verification also exposed stale speculation: every observed section reused
geometry but none reused the route. The queue changed during current motion, and
revisits ended950.91 units from the primary path's predicted endpoint. Preparation
remained3.81–4.87s. This is a demonstrated reason for a further update, not a
blind restart. The preparation watcher now refreshes when next-section targets
change (still one subprocess at a time) and can read the last completed atomic
cache while its replacement computes. The executor joins the first actual pass,
skipping the speculative starting connector, rather than walking backward to an
old origin.75 source tests, including a far actual-origin regression, and root
build pass:821ad8929f304be2a9bd03d11584f941. Final live acceptance pending.
Installed durable841 verified files,11 changed. Collector19816 birth
18:35:41.5575345Z/Gamma15996 birth18:35:45.2906846Z, collection18:36:13 UTC.
Prior3568/20516 closed by the agent for this observed defect, not a user stop.
Own stage removed;75 tests/rootbuild pass; final live acceptance pending.

Final821ad live acceptance18:41:19: second section actually uses both background
path and geometry (`background_plan=true`, `background_geometry=true`). Section
preparation0.791s versus4.744s first section/old6–7s. Actual guarded replan
finalization gaps0.188/0.198/0.192/0.357s versus old2.42–2.75s. Runtime movement,
exact snapshots and current server ACK continue, not merely a running process.
Complete centre18:38:52 UTC has1804 supported zone identities, every1804 present
active on server, zero unexplained missing/older extras;12 newer opens.20absent
retirements accepted,18 still inactive/two later real positives protected.
The live current temple has61 active known shops and8 pending for this pass;
their actual grouped visit and full pass remain under monitoring.
