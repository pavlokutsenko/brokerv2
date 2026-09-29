# Background calculation while moving — 2026-09-27

Current installed buildc90e5616afd9455eb5cf4bf5bf897386 at19:54:34UTC,
Collector2924 birth19:54:34.2808739Z / new Game birth in watch state.
Agent updates are authorized;20244/6056 closed by agent, not user stop.
Source92 checks/root build plus17 packaged tests passed; stage removed. No full pass.

One native worker and reader span the finite pass; one separate offline planner
refreshes future targets from radar. A rejected current plan keeps valid geometry
and requests an offline current-target plan while native guarded approaches run.
Strict identity/generation/map/capsule validation and cancellation remain.

80d461 actual first current-plan overlap at19:49UTC: Heiko and JabaTrader2 read
on two passing arcs during8.158s productive movement, stationaryWaitSeconds0,
accepted=true. Previously9941 Binansovna approach2.57s left4.357s cache wait.
Latest80d source90tests/rootbuild plus15 packaged tests passed; stage removed.
Window command atomic replacement previously raised PermissionError19:40:14,
ending owned reader. Now transient OSError retries the same command within the
10s idle deadline, and C# final event-tail drain runs even after worker failure.

But next actual80d boundary19:50:17 had5.814s ack-to-next-move and5.117s
preparation. All20 remaining targets were farther than the arbitrary700-unit
productive-approach limit. No safe route was rejected: distance alone prevented
any background request, forcing synchronous full price_route on native worker.
This is further measured evidence, not a speculative restart.

The replacement removes that distance cap. It still requires a mapped safe
passing route, native sweep before every ordinary goal, all follower guards,
max25s productive preparation and bounded5s fallback. Follower yields a transit
without stopping once atomic background output appears after at least one native
move. The full route reconnects from the observed actual position; no need to
finish a distant speculative visit. Cancellation/selection/jump/error checks win
before handoff. Failed plan retains bounded guarded fallback, never unsafe motion.
92 tests include farther-than700 transit, mid-transit plan readiness, strict
cancel priority, no stop/drift delay at handoff, and restored callback ownership.
LocalCycle smoke27Sep19:50 passed durable prices/current ACK/history, crash/tail,
centre200 proof/retention, temple65 grouping/new ahead priority. Live far-transit
acceptance still required after publication.

Fresh80d full native centre19:48:46.294501UTC, observer82598.742841/148389.408757
inside200,2163 identities/1866 supported zone shops: whole server active roster
has zero missing. Five extras have positive observations newer than centre;
legacyZLL outside polygon retains historical storage and is excluded scoped UI.
HoleyMole/Lecsi/A1dka/Torgashik227/MS23 inactive; both requested corner shops
closed on fresh native radar/server. Do not fabricate approaches to closed shops.
At19:50:57UTC24 exactshops119rows24currentACK,0 price receipts pending;
broker outbox1286→693 (actual backlog drains), not price delivery failure.

Evidence read-only in LocalAppData/PriceCheckCollector/research/collection-live-watch:
live-acceptance-current.json, center-directory-audit-80d461.json,
full-roster-80d461.json; current owner/birth/log/cursor/recovery only state.json.
Historical design and timings: CONTINUOUS_ROUTE_SESSION.md. Next: prove far
transit actual motion during current offline work, plan-ready no-stop join,
same reader across boundaries/currentACK, actual temple pending targets one
visit, eventual outbox drain/full finite pass. Keep heartbeat quiet on progress.


c90e installed19:54:34.2808739UTC, Collector2924; owned Game6188 birth
19:54:38.5118055Z, collectionstarted19:55:07.2900406UTC. Scope/name/birth checked,
recovery=false. Prior80d finished50exact221rows49currentACK/1historical,
outbox0 before agent update; fullpass not complete. c90e source92 tests14.124s,
17 packaged checks5.163s, rootbuild/sourcepackagehashes passed,854 durable files,
64changed; stage removed. First centre arrival19:55:57; broker19:55:59.
Fresh c90e plan-while-transit acceptance pending.

Live c90e acceptance19:57–20:00UTC:
- First current-plan overlap4.609s, two exact shops,0stationarycachewait,
  background_plan_ready ordinaryhandoff withoutstop; mainread/currentACK follows.
- Next cachedsection prep0.473s, boundary1.410s.
- Invalidated2-targetsection priorityIIXEXOII was1783.024 units away, beyond
  old700 cap. While fullcurrentofflinecomputed,9 guardedordinarymoves traveled
  350.99 units in3.058s, then background_plan_ready/handoff=true, no stop/drift.
  Fullplan accepted fromactualposition,0cachewait; target read19:59:26.993,
  currentserverACK19:59:27.142. Boundary1.072s vsold5.814s.
- Fourth12-targetsection alsooverlapped5.212s/0cachewait, prep1.143s,
  boundary1.584s. One nativeworker6680/oneinstalledreader reused across4sections.
- Actualstalledsegment20:00:25 triggersboundedrecover/revisit; Otus,MarMeJlaD,
  Zahlmeister thenread/currentACK. This is physicalnavigation, notloading pause;
  do not claimeverymovementpause eliminated.29exact129rows29currentACK at20:00:46,
  nohistorical/pendingpriceACK; brokerqueue1449→977→253 withzero retries.

Freshc90e completecentre19:57:48.846028UTC observer82597.618683/148310.009745
inside200,2171identities1873supportedzoneshops; wholeactive server1878 has
0missing,4newerpositiveobservations pluslegacyoutsideZLL explainextras.
HoleyMole/Lecsi/A1dka/Torgashik227/MS23 inactive. Bothcornerclosednative/server.
Privatefull-roster-c90e56.json/center-directory-audit-c90e56.json.
Source/packagedchecks plusreal fartransit read/ACK now accepted. Fullfinitepass,
actualtempleoneentry/allpendingcoverage and finaldrain remainunderwatch; no full
pass claimed. Neither syntheticproddata nor unguardednativeactions used.

20:02:22UTC c90e continues same owned Collector2924/Game6188, no routeERROR;
44exact205rows42currentACK+2historicalACK (not currentprices), outbox0.
Finitepass stillactive (latest1-targetsection20:02:18); no completion claimed.
Historical receipts must remain separately counted; latest generation rereads
handled by durable queue. Watchstate updated counts/cursor withoutsecrets.

First full finitepass c28f122a12bd4c109d62f274b2d562a1 completes20:08:37.547749UTC,
centre20:08:55.881,nextbroker20:08:57.281. Scope counts ONLY first persistent
native stream route-557e433ae00048b1b2e2cc3285615c43:79exact,78unique traders,
366rows,74currentACK+5historicalACK,0pendingpriceACK. Outbox0 was proved before
nextbroker; newcyclebrokerbacklog is not failed firstpass delivery. Three bounded
unresolved names: NIFNIF,ODOHAR,UNCLEIROH (twoattempts/nofreshnearby, no market
removal from roaming). Secondbroker alreadystartsnewpass039c..., do not combine
allcollection-run totals83/377 withfirstfinitepass totals79/366.

MonitorPAUSED at firstfullpass per explicit heartbeat condition, Collector runs.
Active authorized turn finishes further measured detour fix; no blind restart.

Additional temple cause19:59:35 maincoverage is interrupted by new Salershot
outside radar detour. Planned full main path exits temple once, but detourrejoin
uses OLDprogress+offset backintoinside corridor after actualoutsidefinish20:00:04.
Actualreentry20:00:19/stalled20:00:25 unnecessary; TODDO andIIXEXOII were both
alreadyexact/currentACK19:59:23–27. LaterDaergar visit20:02:48 is separateactual
lateopen20:00:25, not evidence oldoriginalpending group was omitted. The measured
4.214s boundary includes ordinaryescapephase actual25.07units motion20:02:57–58
before firstmove_command; it is not4.214s pureloading/cachestand.

Minimal sourcefix: after completed boundedradardetour, keep actualposition+reader
and return replan_required to existing bounded revisit of everyunreadassignment,
instead of reconnecting to stale inside main arc. Nativecancellationguards stay,
unreadcoverage preserved by existinglocal-section revisit; capturedshops skipped.
No newnativecalls/DLL/offsets.93checks pass13.499s, including newexterior-detour
regression forbiddingoldrejoin, and existing read-before-anchor-escape test updated
to preserve actualreadfirst then callerreplan. Rootbuild underway; live newdetour
continuation proof stillpending. Do not claimall temple loops gone fromunittests.
