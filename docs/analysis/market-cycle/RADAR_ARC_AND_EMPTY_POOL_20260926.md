# New-shop arcs and empty-pass loop (V47)

2026-09-26. User reports new shops get a straight close approach, then reports
the character stationary with server pending0, radar pending0, pass read34.

## V46 empty operation loop: confirmed and fixed

`BrokerCompleted` removed known names before async broker upload finished.
An intervening UI tick still considered Phase=Broker inventory and reinserted
them. Reading phase did not purge them. UI filtered these names and showed0;
StepCycle tested the raw pool and repeatedly started empty workers instead
of returning to center. Exact capture/server readiness was not the problem.

Artifact `route-8158f52c82b44268aa408bc0095fc714`:0targets/0requests,
`.radar.json`1672names, ALL1672already in its brokerKeys. Multiple subsequent
empty operations prove the loop. Owner12524 closed normally14:40; no second
reader or ad hoc native probe was used.

V47 has an explicit broker-observation flag turned off BEFORE async handoff,
purges known pool names in reading phase, and independently uses `NewTargets`
for scheduling, frame writing and UI. Known-only leftovers cannot start a
worker even if a late broker tick inserted them. The isolated test reproduces
the intervening tick and asserts the purge/empty new-candidate result.

## V46 approach evidence and V47 arc preference

Seven V46 new exact shops requested at63.0–80.4units, not physical contact:
LooTTrader75.5,DedGirana66.3,Tatorin67.1,TradePlease64.9,BidOne74.1/80.4,
BackStory68.3,BrooklynBaby63.0. However radar detours/revisits used an endpoint
approach at68, often with a stationary read. User wants moving passes.

`radar_pass_plan.py` first tries either tangent entry, radius72short45-degree
arc and110unit tangent departure. A55unit exclusion prevents connectors or
lookahead cutting across the shop. Choose the shorter option with onward
cost; no full circle. Current city geometry and native movement guards remain.
No clear arc or already within95 => existing bounded endpoint68/40/25.

Both moving radar detours and individual new-shop revisits prefer this plan.
`pass_goal_key` keeps moving through the arc after exact capture and hands off
without stop at its departure. Only ordinary individual approaches stop early
on capture. A missed revisit arc gets one endpoint approach within the same
55s/600s budget. Pool candidates without initial native admission also get55s
maximum; each actual request still requires the matching live actor/type/class,
local/server range<=95 and valid exact response. Target cancel is unchanged.

## Verification and current state

-92geometry tests passed: tangent/radius/forward departure, obstacle-side
  choice, narrow-corridor fallback, successful arc no-stop handoff, existing
  rejoin/endpoint safeguards. Fakes only; no game access.
-ModuleIsolation passed, including late broker tick regression, pooled handoff,
  counters across batches and reset at next broker.
-`build.ps1`:801verified files, stage
  `workspace/market-cycle-v47-radar-arc-stage`, logs `v47-*-tests.log`/`v47-build.log`.
-V47 deployment and actual cycles must be recorded below. Tests alone do not
  establish arc reading, loop elimination or full return/broker transitions.

V47 deployed14:43:801verified files,18installed/restored. Owner19644/game1112
(rediscover), collection enabled. First center `b6a8b42970df488a90e7ead14ea479a5`
completed14:45:29, final82619.3,147961.3 inside center500. Broker
`8008cdc1292b4f989e8a345324daf370` starts14:45:34, initial connection setup.

User V46 screenshot proves local counters persisted across route batches:
pass new found18/pool0/read34 while server waiting0. It also exposes that the
pool-scheduling predicate contradicted its visible0 count. Fix now uses the
same filtered snapshot for UI and scheduling/frame input.

V46 successful part: first route d8d29954 accepted25/25current145rows
(18sell98rows,6buy43,1package4),0timeouts/invalid/errors,25cancels/replies.
DedGirana/LooTTrader were observed during broker and both admitted as current
exact server shops in .436/.557s. See BROKER_RADAR_POOL_20260926.md.
V46 never completed its center transition due to the empty-pool loop above.

## Live follow-up

Observed V47 two complete market cycles. First: bb5e63e39d53497798573a169b3f4a54
and 9e54492c0f5e4bc0a13c9037b2e8335b produced33current exact shops/194rows.
Center bac873eab77f4f689e7c2780968c3880 completed14:52:05 local; next broker
and d2d19273a77345b4b15e74c3bda71100 followed without an interval.
Second route completed15:00:05 with36exact shops, then center
c9e834b7a471459ca39bb16cbf331ed7 at15:00:32 and next broker. Audit15:01:
35current shops/223rows,1historical buy snapshot/1row. Do not equate historical
acceptance with current price verification. B03HECEHUE and SilverShine were
native-unavailable, with no fake checked state or whole-market stop.

BigMuzzy live radar arc planned14:47:48, exact14:47:55 at63.054 local distance,
server74.967; captured handoff=true, stop_drift=null, departure~113from shop.
BlackPearl recheck arc planned14:50:16, exact14:50:21 at~69, server82.59,
stationary_probe=false. Both used the actual short arc and continued movement.
Olimpiakraft initial packet and later native location differed~800units; its
bounded revisit read at the updated binding. No further known-only empty pool
loop occurred. New closure cancellation/reopen follow-up:
SHOP_CLOSE_CANCELLATION_20260926.md.
