# Route smoothness correction, 2026-09-25

The user observed circling and frequent stops during the new collector's live
market pass. Collection was stopped with the ordinary STOP file at 18:57:45;
the active route stopped with `reason=cancelled` and no shop or movement command.
Game 9084 stayed responsive. The collector was then closed normally for release
deployment.

## Diagnosis

The price planner picked each 130-degree fly-by solely by its nearest entry.
It did not consider the exit or the next target, so many chosen arcs doubled
back. Six saved live inputs with 64 claimed targets each produced 4–8 anchors,
2.6–5.4 km paths and 3.2–6.8 cumulative full turns. The plotted trace at
`workspace/route-comparison.png` shows the repeated loops. Motion logs show
mostly continuous progress within routes, with only 0–2.1 seconds of sampled
stationary time per route. The visible longer pauses also occur between short
64-target workers during stop, claim and route setup.

## Change

- Giran price targets now get straight 72-unit offset passes. The planner
  scores a guarded connector and the onward exit, improves the open anchor
  order, and validates 55–85 distance to every anchor after rounding. Center
  routing and the prior standalone four-corner research route are unchanged.
- Anchor spacing is 150; nearby claimed shops remain eligible for opportunistic
  reads. An unreachable anchor is deferred without stopping the pass.
- Server claim, renew and release schemas now allow 128 jobs, and the desktop
  claims 128. This aims to reduce the number of worker boundaries and pauses.
  The server still owns all scheduling and leases.

On six saved real inputs, the new plan reached 8–12 anchors and measured
2.56–3.85 km on five routes versus 3.55–5.35 km before. The sixth was 2.60 km
versus 2.61 km. All planned anchor distances stayed at least 56.4 units and
at most 85; no anchor was out of range. This is offline geometry evidence,
not yet a live smoothness or read-rate result.

Focused WorldGeometry tests (18), BrokerWorker tests (22), API build and
full packaged collector build passed. A release publish first encountered two
idle upload workers holding DLLs; the outbox was empty, those exact workers
were stopped, and `build.ps1 -SkipBroker` then passed with matching DLL hash.
Collector 19320 and game 19840 were launched at 19:11 for live validation.

## Live checks pending

Confirm actual target/read distance, route progress without circles, bounded
pauses at batch boundaries, exact server acceptance, no new crashes, and a
complete available-target pass with immediate broker restart. If short straight
passes miss too many shops, lengthen the near-target corridor without restoring
the old 130-degree loops.

## 19:35 update: larger coverage and the remaining setup pause

The user confirmed that stops remained after the first 128-target deployment.
Motion samples in a completed 128-target route showed no stationary run over
0.63 seconds while following the path. The long visible stops were at worker
boundaries: repeatedly rebuilding the navigation grid for each shop exclusion
disk took about 8 seconds in an offline profile and 9–12 seconds in live plans.

The price planner now installs the union of all 55-unit exclusion disks once.
On a saved 128-target input it fell from 7.79 to 1.24 seconds, with all 15
anchors still 58–72 units from the checked path. At the user's suggestion,
server and collector claim/renew/release limits are 500, route allowance is
600 seconds, and the planner selects up to 64 spaced anchors. An offline union
of recent real inputs had 500 targets: 52 reachable anchors, 439 targets
within 85 of the 17.9 km path, calculated in 2.56 seconds. Targets outside
the corridor remain eligible later in the same broker pass.

Live collector/game 16068/8024 started at 19:24. Its first full broker pass
reported 1783 traders. Two complete 500-target routes followed without a
return to center:

| Route | Duration | Shops | Exact | Recovery | Server evidence |
| --- | ---: | ---: | ---: | ---: | --- |
| `route-5c65687ce6534ffa9c62bf134134f186` | 151 s | 118 | 82 | 1 | 82 `wire_int64` current snapshots with its external ID |
| `route-2b572d0011584401b2bb7c057c635e5b` | 161 s | 126 | 96 | 1 | 96 `wire_int64` current snapshots with its external ID |

The shared queue moved from 1195 pending before the first route to 1026 after
the second; the 178 exact uploads all persisted. The third route was stopped
for deployment and preserved its one exact capture.

The remaining 11–12 second setup pause came from the execution navigator:
`cycle_route.py` added its 64 blockers separately after shop hooks were ready.
It now adds their union once. Offline construction including all 64 blockers
measured 0.73 seconds. All 29 WorldGeometry tests, API build, and staged and
release collector builds passed. Collector 828 started at 19:35 with this
change; its game PID and live boundary time must be checked. Full pass
exhaustion, center return and next broker are still unverified.

The parent collector had also launched eight redundant upload helpers in the
prior 128-target run, locking the release DLLs after its exit. It now keeps
the live helper `Process` and launches another only after that helper exits.
The 500-target run kept one helper, and its outbox was empty after both routes.

## 19:49 update: live boundary and stairway position guard

Collector/game 828/3660 started another broker pass (1784 traders). Its first
500-target route `route-f88a3feb5eca40049fcbb06dfe396a1f` began 4.1 seconds
after input creation, versus about 14–16 seconds on the prior worker. It ended
`recovery_unreachable` after four stalls near a tight area, but the next route
started from the current position two seconds later. 28 exact captures from
that partial route reached the server. Route
`route-9c934fb90c394b07b1daa76f1ac2e9e8` completed in 155 seconds with
103 shops and 59 raw exact captures; 58 reached the server. The one filtered
capture was EnanaPro: broker binding ObjectID 1249985886, live response ObjectID
1260467453, so it could not safely complete the old lease. Route
`route-608cf37fdd67454c99675d7fa571f15b` completed in 158 seconds with
114 shops/72 exact, all 72 confirmed in the database. Each of those two full
routes needed four bounded stall recoveries. The shared queue fell to 883
pending after the third route.

The following route stopped the cycle with `position_jump` after 39.7 seconds,
having saved 19 exact captures (all 19 confirmed). It was going down stairs:
Z changed about 57 units; the guard only compares XY. The latest logged XY
sample to settled position was 133 units apart, but native capsule checks and
shop operations delayed the next observation while normal movement continued.
The fixed 120-unit per-sample threshold therefore falsely stopped the whole
cycle. `walk_follow.py` now allows distance proportional to the actual time
between observations and logs distance, gap and coordinates for any real jump.
The 29 WorldGeometry tests, staged build and release build passed. Collector
3000 launched at 19:49 with this correction; live validation and whole-pass
exhaustion are still pending. Rediscover its game PID.
