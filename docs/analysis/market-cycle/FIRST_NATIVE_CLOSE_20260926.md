# Standing before first native shop observation — V51

2026-09-26, about 15:58 local. User's Mpakalogata screenshot confirmed a standing
candidate remained in the new radar pool and was approached. V50 source only
checked native kiosk=0 for actors previously seen in the trading-only native list.
A candidate standing before that first scan was filtered out, so cancellation
waited for a later CharacterInfo packet or nearby absence classification.

Live artifacts (profile `6a0358a7984d4ef18570283b79c3cf88`, LocalAppData runs):
`route-468e7ddd438e46faa126c17415a85c7f` and
`route-3202c65dad0b417e9602b4ad96c4bc61` nominate Mpakalogata OID1226847577
15:51:31/35 without native trading admission. Later route
`98bceacdef3741d0b03a02fd9b16c564` received positive packet closure15:54:49,
then actual native trading admission after a newer reopen15:55:27. No deletion
is inferred from closure or visibility loss.

V51 uses the SAME worker/scoped actor scan with optional kiosk=0 observations.
Only matching queued name/OID is inspected again by existing guarded fresh read
(class/root/name/OID/type). Confirmed zero cancels immediately, including the
first observation; failed guards/absence/different generation cannot cancel.
The existing stream forwards closure to the local radar pool, blocks older
positive packets and allows a newer reopen. No second reader, new memory offsets,
requests to standing players, fake checked/deferred or server deletion.

Also AcceptExact updates runtime binding after a durable exact capture. The next
broker confirming that same generation no longer reports a false REOPENED;
an unread later generation still does. Runtime dictionary operations are locked.

99 geometry tests and ModuleIsolation passed before final targeted scan test;
build.ps1 stage `workspace/market-cycle-v51-first-close-stage` in progress.
V51 live first-observation cancellation and complete repeated cycles still pending.

Final V51published16:03:40, owner13416/game11220. Includes monotonic closure timestamp guards: old closed packets cannot downgrade fresh native closure or cancel a newer generation. Targeted optional-zero scan test and ModuleIsolation passed. Shared smoke fixture Dictionary race fixed in fake maps only (stack Fakes.cs:70). Live validation pending.

V51 first full cycle LIVE: broker29c3d9d complete16:06:41.980; price route d0c0a8dbd87547449a09782d3a95a1c7 finished16:12:48:24/24 current138rows (21sell129rows,2buy5,1package4),48requests/24confirmed target cancels,0invalid/unverified/timeouts/errors. Mpakalogata reopened and read20rows16:08:17.245, serverCOMPLETED/current. xIIIty4kax native0closure16:08:01 (previously seen trading; UIframe removed); Podliwa native0closure16:09:04 no requests. New Biber2/Short never native-loaded, nearby absence skipped; Monya recovery_limit15.956s remains geometry limitation. Center91cb49e1 finished16:13:08.327; next fullbroker7f9348be started16:13:20.346 after settle/worker startup, complete16:13:44.311. All24 current prices stillCOMPLETED; baseline19/19 exact same revision and24hdue, workspace/v51-before/after-broker-confirmation.json. Further V51cycles/first-native-already0 newcandidate branch pending.
