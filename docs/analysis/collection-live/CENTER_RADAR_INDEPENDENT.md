# Independent center radar reconciliation — 2026-09-27

User clarified: on launch and every return, go to center, collect broker,
reconcile closed traders from radar, then read prices. Preserve this order.

Collector7188 / Gamma24672 survived the earlier launcher-root failure point.
By15:19 UTC, individual exact price acknowledgements were progressing and
outbox was empty. The broker returned1834/1834 responses but ObjectID1231113113
(one item39505 row) had no native/packet identity. That made the enriched
inventory incomplete and also, incorrectly, prevented center absence cleanup.
HoleyMole and Lecsi therefore remained active on the website. The raw final
actor observation had1969 accepted player identities and no rejected ones;
1733 were trading. Do not retroactively label this older file a complete radar:
it lacks the new explicit completeness proof.

The worker now exports separate native_radar evidence from a full current actor
capture. It confirms a stable actor array across field traversal, unchanged
world/level/controller/player, stationary observer, no failed diagnostic reads,
and no rejected player identities. A wanted-ID-only capture cannot be complete.
Cached diagnostic accessor defaults after failed reads are counted, so unread
objects cannot silently prove absence. Offsets/class/module guards are retained;
no native payload or driver change is required.

CenterRadarEvidence validates current PID, capture after this phase started,
freshness5 seconds, center500, nonempty complete identity list, unique IDs,
matching individual observation timestamps/observer coordinates and positions.
Legacy workers, partial captures, stale evidence and roaming cannot retire.
Broker row completeness remains independent: unknown rows stay unresolved and
never acquire an invented nickname, quantity or price. Reconciliation runs
after each broker phase, before the next price pass, using the separate proof.
The existing store and server still protect newer positive observations/reads,
retain history and remove only current offers for confirmed absent identities.

Validation:12 focused Python tests (center evidence and broker bindings) pass;
LocalCycle.Smoke including history/reopen/outbox and independent native radar
gates passes. System dotnet has no SDK; use C:\tools\dev\.tools\dotnet\dotnet.exe.
Root build.ps1 passed with isolated output in workspace/center-independent-build,
BuildId eac599912525445da470e51626eaf527. Fresh live cleanup and production
HoleyMole/Lecsi inactive responses must still be verified after deployment.

Live deployment: the main Collector7188 closed normally, detached its reader
and stopped only its owned game24672. Its remaining upload-worker24364 was
identified by parent/start time and stopped before durable publication; queued
data is retained. Canonical apps and both ZIPs now carry the new BuildId.
DesktopPackages smoke passes for both real portable EXEs and PS5.1 verification,
with production settings unchanged. New Collector23520 / game24088 launched
the saved character, reader attached, collection started15:22:19 UTC.
Returned to center15:23:03 and broker began immediately.

Broker adc8633dfa1446b0b35ab9bf863a1b37 finished1847/1847 replies at15:24:56.
One old unbound row remained, but the separate native_radar proof is complete:
4139 actors,1940 player identities, no rejected identities, stable observer at
(82378.645,147941.962). At15:24:57,697 old absent identities were retired locally
and queued as center_absent. Both HoleyMole and Lecsi retain their original price
history and now have ConfirmedClosed=true with state15:24:56.597112 UTC.
Initial server verification: Lecsi already inactive, HoleyMole still pending
while state delivery drained. This was sequential delivery, not failed removal.
Final history audit requested all697 retired keys:637 were returned by the
server and all637 are inactive; no returned active identity remains. The60
unreturned keys are absent from server history, not proven637 extra deletions.
HoleyMole and Lecsi both inactive with state15:24:56 UTC and old prices retained.
State outbox drained without retry/error; individual HTTP delivery of697 events
caused roughly a minute of gradual disappearance. User confirmed they vanished.
Browser reload verified4 arbitrage items, without Chest with Life Stone,
HoleyMole, Lecsi or FermerDjon. Screenshot:
workspace/center-radar-arbitrage-cleaned.png. This confirms displayed offers as
well as server state; no manual DB deletion or fabricated closure was used.
Price route86013a00efbe4556a6800d0bf0b4a6c5 started1285 initial targets and
new exact captures progressed immediately. Northeast live acceptance is pending.
By15:27 UTC the new route had96 exact snapshots /447 rows,95 server price
acknowledgements,97 attempted route entries,1287 admitted targets. Remaining
broker quantity uploads were draining without retries; state queue was empty.
Collector and game remained live. Heartbeat continues monitoring the full pass
and the two corner shops, with this acceptance recorded in its private cursor.
