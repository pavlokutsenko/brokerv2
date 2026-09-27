# Individual rechecks, 2026-09-26

User correction: route/read failures should stop and replan, not defer. Closer
ordinary approaches are allowed when a normal path cannot be found.

V24 eliminated the 495-job radar return abort. First 500-target segment
adbd51135f0341dea2c38300f9a29cec produced 196/196 current exact snapshots
(158 sell, 21 package, 17 buy) and only one missed anchor versus 42 in v22.
GNOM4U reproduced an endpoint unable to rejoin; the preplanned reverse return
read it and rejoined successfully. Next segment 1fb0f1858d8c4bdea640d0d5480ab89d
produced 164 exact reads but left UEBANXXX/no passing path, CHIPS/CITRINE/missed
windows and four no-reply requests. Later temple segment fe47ceae71424e90849389f5a7d030ac
ended recovery_limit after 108 exact reads; no price decoder error or timeout.

Root causes remaining: old revisit excluded every sent request even without a
reply, selected at most four live anchors, and never retried static planner
failures. C# removed misses from this pass and imposed server failure backoff.

V26 now stops and builds individual plans for all failed anchors and observed
unread windows/requests. Success is an exact capture, not request submission.
Shared anchor disks are not reused. It tries 68, 40, then 25-unit destinations;
if projected map has no route within 500, a bounded ordinary direct movement
probe lets game collision resolve it. Stops/drift checks, cancellation/world/
identity guards and exact wire verification remain. At the read point the same
reader waits up to four seconds; zero-distance ordinary movement refreshes
server origins, and ordinary request/target cancellation is unchanged.

Unread leases are returned without an error/backoff and remain ready on the
server. Transient execution replans are capped at three per pass, then held
for diagnosis while the other targets finish. If those remain, collection shows
names and stops before reporting pass exhaustion/return. They are not silently
marked departed, and no absence deletion is introduced. Server still owns
all deadlines, leases and authoritative queue state.

54 tests passed; build.ps1 succeeded to workspace/market-cycle-v26-recheck-stage.
Includes v25 center 470 destination stopping margin. Live validation pending.
Do not start a second reader. Update results below as real runs finish.

V26 deployed at 10:32, collector 10428/game 10448, one reader. Durable publisher verified 775 files; restored 20 changed files. Requeued 82 old skips: 57 recovery_limit, 13 no replies, six too-close, four missed windows, two no-pass. Snapshot workspace/requeued-skips-20260926-v26.json. Live pending.

10:44 user observed back-and-forth; STOP sent immediately, then closed the one
collector/game for deployment. V26 route d324e226d70a4e629c5d1538de6e8018 was
cancelled after 114/114 current exact snapshots (91 sell, 15 buy, eight package).
68/82 requeued shops became current; four absent from complete broker, ten ready.
The individual recheck read FreeStyler, ISUR, ShmonayuTrupy, Arkom, CITRINE,
Trader6, Extass, V1lka, Financi1, JohnyDoDep and others. Remaining navigation
problems were real, not hidden by the ready-state change.

Saved section map was cropped to X80350..84600, Y146950..150000 while the actual
saved scene covers X79900..84600,Y146500..150900. UebanXXX (83163,150520) and
Entombment (81728,146766) lay outside the navigation crop. User requested map
expansion: rebuilt sections, capsule bands and ground offline from that scene,
no second reader or broad live capture. All 1688 active traders fall within the
expanded extent (broker bounds X80692..84501,Y146766..150520). City profile now
selects giran-navigation-2026-09-26-expanded.json; original maps retained.
329 obstacles, two unknown entries, 52,156 ground samples, 10,174 missing.

Further backtracking fixes: recalculate nearest remaining recheck target from
current position at every step (old list was sorted only before movement);
finish an individual approach immediately after its exact reply (previously
could continue a long connector after reading); at most two local recoveries
per individual approach. Main route obstacles immediately stop/replan instead
of following a stale main path for 12 recoveries. Price operation exceptions
resume unread targets, not prematurely return to center. Direct ordinary probes
can go beyond a projected map boundary in a locally expanded probe envelope.

V30 stage workspace/market-cycle-v30-expanded-map-stage: 55 tests passed;
full build and actual expanded-map verification pending. Old PIDs 10428/10448
are closed. Rediscover after deployment, one reader only.

V30 build passed; deployed at 10:46 with 778 verified files (23 replaced), collector 9512/game 5468. The server has 15 ready jobs, no deferred errors. Actual remaining coverage and multiple cycles still require observation.

V30 live 11:00: first expanded-map route 6bb763ba9a4c4c98a9ba20075a10656e
captured 48 exact shops (no invalid replies, no timeouts). Main stall triggered
immediate replan; individual read completion stopped approaches. Remaining
ISLM and one neighbor were read on subsequent batches. Only ENTOMBMENT and
UEBANXXX remain ready; after three approach operations collection stopped for
named diagnosis, no artificial server defer and no premature completed pass.
V26 cohort audit reached 73/82 current since reset, five absent from broker.

Geometric obstacles, not memory failures, remain. Ueban direct probe stopped
at (83325,149779,-3473), exactly Giran_V_Plaza_Wall3. Saved wall sections have
an opening X81386..81702 at Y149810. That passage is derived, NOT live validated.
Ueban read-position circle is entirely masked by BSP B3380927/interior_A capsule
bands; actual room floor/stairs and entrance need a bounded check.

V31 source/stage adds escape from inflated departure clearance before each
individual replan and an ordinary room probe preserving other city obstacles;
55 tests/build passed to workspace/market-cycle-v31-room-approach-stage, NOT
DEPLOYED. Relaxed room connector still does not connect offline, so do not
claim this fixes the room.

After automatic collection stopped and BrokerWorker exited, movement-only
checks on existing game 5468 attempted the northern exterior. Direct movement
hit the north wall. A westward direct probe then climbed onto Manor_Giran2:
current pawn ~82070.484,149681.039,-3434.114. Short departures stalled. Eight
bounded native capsule queries at actual capsule height all hit Manor_Giran2;
changing the ordinary move goal Z to the prior plaza height -3473 produced no
movement (rejected hypothesis; no actor coordinate writes). Do not repeat that.
The ordinary probe omitted obstacles, which is why it entered Manor; production
should retain known hard obstacles and use a connector to a confirmed passage.
Game/collector still exist (9512/5468), collection disabled, no BrokerWorker.
One pending screenshot clarification asks user to show current Manor position.
Sky game screenshot capture again timed out. Do not blindly use UI inputs.
Raw evidence: workspace/giran-edges-20260926/*.json, *.motion.jsonl,
workspace/probe-departures.py and grounded-exit.py (diagnostic scripts).
All checks restored their owned hooks; no purchases or actor-position writes.
Next: recover off Manor, confirm north wall opening, enter northern room; check
south approach using expanded geometry and real passage, then resume one owner
and verify several complete cycles. Current market is NOT proven stable.
