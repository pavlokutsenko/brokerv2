# Fresh market run with ten-minute character rotation

2026-09-26 Europe/Kiev. User resumed collection, requested DB reset and a full
market run, then automatic restart after disconnect/failure and a ten-second
pause following client exit. Active UI profile Gamma/Giran
`6a0358a7-984d-4ef1-8570-283b79c3cf88`: rotation10±2minutes, enabled.

## Reset and first run

Stopped UI and verified no game/reader worker or ready outbox entry. Server
maintenance `apps/api/scripts/reset-cycle-market.ts` locks Gamma/Giran, saves
and fsyncs a scoped backup BEFORE deletion inside the transaction, verifies0.
Backup `C:/broker/workspace/v52-market-reset-20260926.json`:
3850 traders,23425 broker rows,3660 queue jobs,5293 price snapshots/26389 exact
rows,2612 ingest batches,57 collector statuses. Item catalog, other scopes,
city geometry/collection zone and user configuration retained.

Owner18132/game12000 started16:53. Center route67c79ca0 completed16:54:52,
broker86f8401a completed16:56:47:1769traders,8684rows,full valid snapshot.
Price route389b8b42 captured378CURRENT exact shops/1771rows through17:02:54:
290sell/1380rows,64buy/286rows,24package/105rows. Deferred0.
17timeouts retried,395confirmed target cancels, no invalid/unverified replies.
Server audit `workspace/v53-before-deploy-market.json` records378COMPLETED.

The first REAL elapsed10±2 timer fired17:02:54 (about8m51after ready).
Reader/route drained, old game exited. The replacement launcher exited-1
without creating lu4.bin; V52 latched rotation failure17:04:24. This differs
from the earlier accelerated seven-slot login smoke. No Fatal memory exception
was observed. Root cause of launcher-1 is not established; user requested10s
pause between clients. Do not claim the first elapsed rotation succeeded.

## V53 changes

- Pause10s AFTER exact owned game generation exits, before replacement launch.
- Early detection of a nonzero launcher exit; retry startup at15/30s, max3.
- Profile AutoRestartEnabled defaults true, UI checkbox; requires Auto login
  and collector ownership. OS TCP/liveness probe uses existing process identity,
  no second game reader. Confirmed connection loss20s, unresponsive60s,
  missing native player position90s, fatal window/process exit/reader failure.
- Recovery retains reader/collection intent; same character on reconnect.
  Failed replacement remains eligible with15..300s backoff. Explicit stop
  forgets intent. Failed rotation can hand recovery its intended next slot.
- Drain before stopping; failed/bounded drain destroys only the owned game,
  awaits old worker teardown, then retries detach before new reader.
- Revisit used arcs ONLY for radar-new shops; broker revisit went straight.
  Now both try tangent+short45degree arc. Being within95 no longer disables it;
  when already nearer55.5 read without circling back. Geometry-blocked arcs
  retain bounded close fallback. Motion log records passing_arc/fallback reason.
- Idle UI close falsely reported reader cleanup failure: synchronous cleanup
  called Close inside WPF's Closing notification. Queue Close on Dispatcher
  after that notification unwinds. Real errors now include reason/stack in
  LocalAppData/logs/collector-PID.log. Regression smoke reproduces idle close.

## Checks and current deployment

100geometry tests passed; ModuleIsolation includes disconnect grace, crash,
same slot, drain ordering, retry after login failure, explicit stop, rotation
pause invocation. Actual UI smoke checks settings and idle close. No packages.
V53 built/published805verified files, final close patch republished17:09.
Installed owner20848 launched17:09:53 with normal launch+collect flags.
No second reader/purchases. Retain378existing prices; continue original full
market cohort and validate real elapsed rotation/recovery after deployment.
Full run and several cycles are still in progress; do not call collection ready.

17:15 update: after restarting in the prior character's saved market position,
one center escape failed at83701,147926,-3409; retry returned safely and full
broker8b385235 completed17:13:36,1868/1868replies,1770traders. Baseline373/378
kept identical revision and24hdeadline;5are absent from active/completed rows
after authoritative full broker. The17:17inactive audit confirms all5CANCELLED
with removedAt17:13:35.680: THEDANGER,VORGOVNA,ANCIENTLUCIFER,BAPKYIIIKA,
DEALERYOURHAPPY. No false requeue for the373unchanged current shops.
New route606e568f running,404COMPLETED,0Deferred at17:15:10.
17:18:13:547checked,1229pending,0Deferred. Real recheck plans include both
broker and radar passing_arc=true. Recent78plans:31arcs,47already_too_close
(existing distance<55.5), noblocked_arc fallback in that sample. Nearby readings
often complete0.2–0.7s; examples2.6/5.4s include travel. No extra loop is forced
for a shop already beside the moving character. Next elapsed rotation17:22:20.

## 17:35 — V54 dwarf navigation and real V53 recovery evidence

The natural 10±2 minute V53 timer drained the human route at17:22:25;
character1/7 GrownMan was connected at17:23:04.967. Character0→1 passed.
The controlled OS TCP disconnect at17:23:37.796 (PID21224) triggered recovery
at17:23:58.834. PID19976 connected at17:24:38.177, same character1, collection
intent restored. See workspace/v53-disconnect-test.json and collector-20848.log.
Accepted server prices survived (699checked); no second reader or purchases.

Movement then exposed a separate hardcoded human capsule requirement9×23.
Three center attempts stopped collection. A finite, read-only established-driver
inspection proved GrownMan capsule9×18, scale1,1,1; artifact
workspace/v53-dwarf-capsule.json, tools/WorldGeometry/inspect_navigation_capsule.py.
V54 accepts only native-confirmed9×18 or9×23. Floor uses actual half-height;
level clearance probes retain the conservative human9×23 map envelope.
103geometry tests including dwarf readiness/floor passed, ModuleIsolation passed.
Canceled rotation routes no longer count unread cancellation as approach failure.

Old owner20848 closed normally (idle-close fix confirmed; owned game exited).
V54 published810verified files; owner19856 started17:34:49 with explicit
--resume-character plus Gamma launch/collection flags, to continue saved slot1.
Normal UI/startup launch still begins slot0. Operator resume avoids resetting the
roster on an update. Full cohort exhaustion and dwarf live movement still pending.

17:43 V54 live dwarf proof: center route f1f658a7 completed17:36:03
(11.204seconds movement,22commands,1103traveled; peak route error2.34).
Full broker d03ae8a9 completed17:37:56,1912/1912replies,1776traders.
Price route390bef09 now200CURRENTshops/1090rows, server875checked/907pending,
0Deferred. Recheck samples42passing arcs,18already-nearby reads,1blocked arc;
failed arc retains bounded close fallback. Bagrov/Centu/Reata were skipped only
after actual read-point native absence, fresh scans and no outstanding request.
No server deferred/delete follows those local skips. Next full broker must confirm
presence; a native absence alone is not proof of global closure.
Audit v54-after-broker-market.json compared to469baseline at17:17:
452same revision/due;11absent/CANCELLED;3MOVED ready for recheck;3rechecked later
(2changed inventory,1same fingerprint with newer exact completion). Comparison
artifact workspace/v54-preservation-comparison.json. Full cohort still running.

17:53: native rotation1→2 completed17:47:05 (Papaur, PID18328).
No false approach replans were logged for the canceled timer route eb581923;
it still safely spooled80CURRENTshops/364rows and86confirmed target cancels.
Second dwarf center fa989b89 completed19.058seconds movement, followed by full
broker07da9c32:1908/1908replies,1763traders at17:49:39. New radar pool was active
through broker. Next price route c96ee2aa has132CURRENTshops/681rows; current
server1084checked/682pending/0Deferred. GetTheTorg/h8u2, locally unavailable
on the preceding route, were accepted on the following route, showing renewed
native presence remains eligible. Bagrov/Centu/Reata were already absent and
CANCELLED by the authoritative preceding full broker, not by the native skip.

Pause observation v54-rotation-pause.jsonl measured a9.123s LOWER BOUND from
late process-list removal to new launcher. Windows still enumerates terminated
processes briefly, so this is not an exact exit timestamp. The production guard
uses HasExited and then awaits10s. Next passive observer uses the cached old
Process.HasExited to align measurement; no additional restart is induced.

17:57: natural rotation2→3 completed17:57:06.790, Aexetan PID20156.
The corrected passive HasExited observer recorded old exit first seen
14:56:27.141586Z and replacement launcher started14:56:37.039177Z:
9.897591s conservative lower bound with200ms observation step, consistent
with enforced10second delay after exact HasExited. Artifact
workspace/v54-rotation-pause-18328.jsonl. No induced extra restart.
Before switching1194checked/579pending/0Deferred. Route c96ee2aa cancellation
spooled all captured prices and returned remaining leases; no false approach
replans on cancellation. Current female dwarf center b41cc5c2 under observation.

18:02 V55: female dwarf Aexetan navigation did not start because actual capsule
is5×19 (scale1), confirmed by finite read-only established driver inspection:
workspace/v54-female-capsule.json. No new memory offsets or game functions.
Added the third native-confirmed shape, retained conservative9×23map/sweeps and
actual half-height floor. Added female floor/level-sweep regression;104geometry
tests passed. build.ps1 full native/worker/managed build passed,810verified files,
8changed installed. Owner21188 started18:01:29 with --resume-character slot3;
reader connected PID13108 at18:02:00. Old owner/game closed normally.1194accepted
prices retained. Real female movement/full cohort still under observation.

## V56 — late passing misses, 18:16 deployment

Live logs showed half pairs at the edge: e.g. IamHotYouAreNot action1 at93.28,
action2 skipped at99.41; xxMarkeTxx similarly. Full radius95guards behaved
correctly. V56 starts action1 only within87, reserving8units for the next frame;
action2 still requires full95 plus native generation, city zone and server-origin
guards. No protocol change, extra game reader or native layout change.
Another miss: broker targets first passed during individual revisits were not in
the initial revisit candidate set. They waited for another500target operation.
V56 admits these newly seen/requested broker targets into the same bounded loop;
handled/captured/unavailable targets cannot be repeated. New regression cases
cover edge rejection, full-radius second action and late miss admission without
repeats.107geometry checks and full build.ps1 passed.

V55 female route90fd5d85 completed221CURRENTshops/1191rows, all231target cancels
confirmed, invalid/unverified0. Server1413checked/371pending/0Deferred before
natural3→4 switch completed18:14:24.571, Tweedle PID11756. Normal UI close then
drained its in-progress center operation and closed owned client without warning.
V56 deployed810verified files/10changes, owner5616 started18:15:46 using saved
slot4 and --resume-character; collector will resume original full market cohort.
Full exhaustion, return/immediate broker and several following cycles pending.

18:26 V56 live proof: Tweedle center3b8339ab completed7.904seconds movement;
full brokerae752081 completed18:18:48,1908/1908replies,1761traders. Price route
5d0be90c continues; server1537checked/231pending/0Deferred at18:26:30.
The later missed broker admission ran in production: recheck_late_broker_added
for LineageIIBank/Rubycoin/Mir7/Sakso, followed by successful readings. Main route
encountered a CharacterNpc_C capsule near82844,147804; the native collision guard
stopped the main path and switched to bounded individual approaches, not a loop.
Bubo was admitted18:24:05, closed(native kiosk=0)18:24:23, admitted again open
18:24:37, proving closed and reopened signals remain separate/eligible.
Current rotation4→5 due18:26:53. Full cohort not yet exhausted.

18:29: natural4→5 completed18:27:38.337 (SmokePlumes PID16740). V56 canceled
price route5d0be90c preserved170CURRENTshops/861rows.171complete request pairs
(342actions),171target cancels and confirmations,1timeout,invalid/unverified0.
All29range skips were action1 before sending; no half-pair/action2range aborts.
This verifies the8unit start margin in live movement. Server1547checked,
221pending,0Deferred,2outside before new full brokerb7280080. Full exhaustion
and several subsequent cycles remain pending; continue running.

## V57 — unbound jobs remain ready,18:41

One Deferred appeared after full brokerb7280080 at18:30. ISLM had no current
runtime binding: absent from final raw1754bindings vs1757broker traders, and
current radar pool. The old claim code released it with an error string, causing
the server's normal2minute retry. No approach or decoder failure occurred. Its
retry expired normally by18:32, but this was the wrong semantics for binding loss.
V57 releases without error/attempt increment, excludes only this pass and logs
missing object vs changed type vs moved coordinates. It cannot mark checked or
delete. No identity, type or coordinate guard was weakened. Synthetic actual
claim/outbox regression verifies error:null, bounded pass skip and diagnostics;
ModuleIsolation incl.rotation/recovery passed; build.ps1 -SkipBroker passed.

Natural5→6 completed18:39:43.755(Octopi PID19396). Old UI5616 then closed normally;
empty outbox verified. V57 durably installed810files/4changes. Owner19052 started
18:40:37 with saved slot6 and --resume-character. Prices are preserved; server
last1634checked/141pending/0Deferred before replacement. Full market exhaustion
and several subsequent cycles still pending. Keep testing; no second reader.

18:48: current V57 Octopi route90d9af75 read hard TinkMyBell successfully at
18:46:28.437. Approach1.02seconds, arrival read0.264s,2actions; local
84192.98,148034.54,-3407,74.51horizontaldistance and1.0verticalgap. The normal
passing arc was blocked, bounded standoff approach succeeded; no direct unsafe
probe was needed. Server1702checked/54pending/0Deferred/1outside at18:48:04.
Earlier hard-target checkpoint limitation is resolved for this actual run.

V58 prepared to include all assigned unread broker targets in initial revisit,
not just anchors/previously seen targets. Main anchor-spacing coverage can leave
a shop outside the actor window even when it is an assigned known broker shop;
it should approach that binding once and still require live matching actor/95
before any request. Radar-only targets require native admission.108geometry
checks and full build passed, stage market-cycle-v58-all-assigned-stage. Not yet
installed; plan publish after next natural6→0 so this current near-complete pass
can finish. Preserve prices and continue actual complete cycles.

18:54: natural6→0 completed18:51:48.591 (parsew PID18648). Actual full roster
circle0→1→2→3→4→5→6→0 is confirmed. The10second post-exit pause is installed.
V57 nearly exhausted current pass at1749checked/10pending/0Deferred/1outside,
then timer canceled/drained it normally. Normal UIclose19052 succeeded; owned
client/workers exited and empty outbox verified. Idle uploader19464 stopped.
V58 durably deployed810verifiedfiles/6changes; owner19048 started18:53:07 with
savedslot0 and --resume-character, reader game19004 connected18:53:37.979.
Auditv58-start-market.json: wire_int64 snapshots across all3kiosk types,
1749COMPLETED,11PENDINGincluding1outside ENTOMBMENT,183CANCELLED historical.
No queue reset/fake completion. Full uninterrupted exhaustion and several subsequent
cycles still required; continue live test.

## User correction and V60 continuation,19:16

User explicitly clarified: new radar shops are fine; the NEXT CHARACTER must
continue the previous character's unfinished pass. The prepared V59 radar60s
fairness cap was NEVER INSTALLED and was removed from source. Do not publish
market-cycle-v59-radar-fairness-stage. Radar discovery/detours stay unchanged.

Root cause: rotation/recovery used Detach→Attach→SetCollection(true), which always
called StartCycle and discarded CycleRun, pooled radar shops and pass counters,
then returned to center and repeated broker. V60 adds a scoped client-change
suspension/continuation path. It drains the old worker, preserves broker batch,
worker lease owner, remaining durable keys, reviewed/captured radar keys and
counters, then claims the remaining server jobs from the new character's location.
No center/broker until the existing pass exhausts. Explicit stop/start still
starts a new pass. Interrupted broker/center safely resumes through center.

Old ObjectIDs are invalidated to0. Destinations only nominate movement. Fresh
radar frames are tagged with current PID; same key/type/position(<20) must rebind
before the existing native class/root/OID/range sender can act. An unbound scan
cannot prove departure. Exact returned captures independently require the same
key/type/position before updating a continuation lease. Server remains queue owner.
ModuleIsolation regression passed including same CycleRun/counters/pool, no old
objects, guarded exact rebinding and explicit-stop semantics.110geometry checks
passed. Full build in progress, stage market-cycle-v60-client-continuation-stage;
actual natural rotation continuation has NOT yet been verified.

19:18: V60 full build passed,812verifiedfiles/20installedchanges. Old UI19048
closed its active price worker normally, released unfinished leases; outbox empty,
idle owned uploader21340 stopped. V60 owner18972 launched19:16:48 savedslot1.
Auditv60-before-deploy-market.json:1761COMPLETED,7PENDINGincluding1outside,
221historicalCANCELLED. All10accessible v58-start leftovers now COMPLETED:
PARO19:15:59;TRIMA19:05:25;MANUCHAO19:05:06;FERNANDOFRUZ19:15:32;
EXTRADER419:05:08;PREDPRINIMATEL19:04:09;MESAPRIME19:05:32;
ZEROTRAIDE19:05:14;BARR19:04:09;MIR718:58:03.
The old initial hard cohort is cleared by actual accepted reads, not reset/skip.
Natural V60 continuation and several full cycles remain under observation.

19:23 exact server acceptance verified by new read-only server script
scripts/audit-route-prices.ts, scoped market/city and external snapshot IDs.
V57route90d9af75:152/152captures stored,643physicalrows; sell124shops506rows,
buy24shops120rows,package4shops17rows. V60currentroutec1baa945:18/18stored,
104rows; sell14/85,buy2/10,package2/9. Exact row index,itemID,itemObjectID,
quantity,enchant,price,basePrice,side,kioskType,precision,traderKey all match.
No missing or mismatched snapshots. Source integer safety checked before BigInt
comparison; no unsafe-number audit can silently pass. Artifactsv57-exact-price-audit.json,
v60-exact-price-audit.json. V60 full broker0f5814fb completed19:20:02.585,
1882/1882replies,1750traders. Natural1→2 due19:28:05; continuation still pending.

## V60 actual handoff and V61/V62 follow-up,19:38

Natural1→2 completed19:28:50.919, game11780, SAME ownerUI18972. Log explicitly
continues previous pass1723broker candidates/4radarshops, no broker/center.
UIv60-rotation-ui.jsonl retains49read,27found,4pending before and after replacement.
Oldroutec1baa945 drained canceled49CURRENTshops/213rows,98actions,49cancels and
confirmed replies,0timeout/invalid/unverified. Newroute7f0e617 read5newshops and
kept the pass counters. This is actual continuation, not a build-only claim.

However two carried destinations HunRedWitch83948,149102 and traderuf83128,149547
hadObjectID0 and no fresh new-PID packet. V60 could only admit positive-ID native
radar, so these two survived across otherwise completed radar-only worker jobs
(7f0e617→ff4d1c5→5177e6), preventing market exhaustion. V61 carries these two
as explicit movement nominations. Within110units only, use the SAME established
coherent/native actor enumeration unfiltered to obtain fresh identity; revalidate
class/root/name/type/location before binding and still apply existing RPC guard.
Standing/type-changed shops skip current pass through positive native evidence.
Absent shops require3freshcomplete scans at actualreadpoint(<100), never a
filtered/unbound absence. No authoritative deletion or fakechecked.113geometry
checks passed, including oldPID rejection, no unboundRPC, bounded nomination,
standing/generation skip and qualified absence. Native offsets/hooks unchanged.

A real recovery occurred19:35:04 for Access to the path is denied; old log lacks
stack so exact original sourcepath is unknown. Windows radar-frame replacement
under an open read handle reproduces the same condition. V62 treats that local
sharing/permission race as retry, reads progress withFileShare.Delete, and saves
full stack for future actual reader faults. Real restart19:35:44 restored same
character2 and SAME pass with3radarshops, providing recovery-continuation proof.
ModuleIsolation locked-frame regression and fullbuildpassed.816files staged at
market-cycle-v62-frame-sharing-stage (includes V61 worker from fullbuild).
Publication in progress; do not reset accepted market, no purchases/secondreader.

19:40: V62 installed816verifiedfiles/14changes. Old UI18972/game7564 closed
normally, empty outbox confirmed; idle owned uploader19616 stopped. NewUI20892
launched19:40:00 using --resume-character savedslot2, Gamma launch+collect flags.
Latest server1790active/1788checked/0pending/0Deferred/2outside. Continue actual
pool exhaustion→center→immediate broker→severalcycles; no furtherDBreset.

## V63 carried closures, 2026-09-26 20:00

Real V62 natural 2→3 at19:50:31.403 and3→4 at20:00:05.117 preserved the
same unfinished pass, broker cohort and radar pool; no intervening center/broker.
Daybreak, previously two missed approaches, rebound to freshPID11120 OID1212177661
and read at19:53:04.862. PostgreSQL accepted it; readonly route788c0980 audit
18/18captures78rows all3kiosk types, zero missing/mismatch(v62-exact-price-audit.json).
Server at19:59:1786active1784checked0pending0Deferred2outside; DB NOT reset again.

Actual live log exposed repeated Inxi continuation_nomination/native closure
19:53:09..15. Source: refresh_radar processed carriedObjectID0 as a new generation
and removed closed_stamps/unavailable before rejecting the0ID. V63 rejects zero
identity before lifecycle changes, preserving movement nomination only. Failing
regression reproduced unavailable removal; fixed lifecycle/session tests10passed.
Second cause: UI NativeClosed rejected a validated fresh worker identity because
pending destination still hadObjectID0. Accept positive fresh identity only for
unbound pending, retain positive-ID mismatch/stale-time rejection; save fresh
closure identity so newer real reopening re-enters. ModuleIsolation passed.
Fullbuild816verifiedfiles stage market-cycle-v63-closure-continuation-stage.
Oldowner20892 normal close requested20:00 after empty outbox. InstallingV63;
operator application replacement starts a fresh broker with accepted server
prices preserved; natural character changes continue one in-memory pass.
Next: actual postV63 pool exhaustion→center→immediatebroker and multiple cycles.

## V64 native command recovery,20:07
V63 owner872 PID19296 return failed20:03:04: CapsuleTrace ProcessEvent timeout,
cleanup bridge trigger1/status0; retry20:03:37 trigger0/status1 (running command).
Client exited20:04:04, automatic same-slot4 restart13944 completed20:04:44;
same phase preserved, successful center return followed by broker20:05:46.
Crash root cause unproven: no recent Windows Application1000/1001 event. This
is NOT an Inxi/radar-memory-offset change. Full traceback in error-20260926-
170304-db7d0ca570144777885a345e81a3c375.log. Do not reset an armed/running
native command. V64 recognizes these two exact transport errors in any cycle
phase, sets ClientFault for owned-client recovery, retains collection intent/
phase and defers new work30s so the same stalled bridge is not reused. Actual
crash recovery preserved phase; named transport branch ModuleIsolation checked
for center/prices, all module checks passed.816files/2changes durably installed.
Oldowner872/recoveredclient13944 normal close, emptyoutbox, idleowneduploader3588
stopped. Newowner launched savedslot4; rediscoverPID. No DBreset. Continue actual
complete postV64cycles; earlier successfulhandoffs remain valid, readyNOTclaimed.
