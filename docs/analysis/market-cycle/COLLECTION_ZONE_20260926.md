# Approved Giran collection boundary — 2026-09-26

User accepted the displayed stepped boundary and requested permanent ignoring
outside it. Stored in maps/Giran/city.json collectionZone revision
giran-2026-09-26-approved:
(80640,147430),(83590,147430),(83590,147020),(84560,147020),
(84560,149830),(80640,149830). Includes the temple/upper extension, all1690
ordinary shops in approved snapshot; excludes only Entombment81728,146766
and UebanXXX83163,150520. Boundary points included. Geometry is city-specific.

Server PostgreSQL city_collection_zones stores the same approved copy. Migration
20260926120000_city_collection_zones and explicit descriptor synchronization
completed after database backup .runtime/before-collection-zone-20260926.dump.
Runtime server has no collector-repo filesystem dependency. Both queue claim
APIs filter by current coordinates; cycle replays/renewal also respect the zone.
State retains broker Active total and reports IgnoredOutsideZone separately;
outside jobs do not inflate Waiting/Deferred/Overdue/Checked. No fake completion,
deletion or new retry deadline. Movement/reopening outside remains excluded;
moving inside restores ordinary eligibility with same durable traderKey.

Packaged worker gates initial targets, radar-new discoveries, dynamic detours,
fresh pre-request actor coordinates and individual revisits. Mid-pass moved
outside targets stop their approach, release normally and leave current visit
candidates without being counted as errors. This boundary constrains shop
selection, not movement geometry; an obstacle detour may use the exterior.
The Manor hard exclusion still applies to every movement path.

63 WorldGeometry tests passed including boundary edge/temple extension/outliers,
live-request and radar-detour guards. Policy unit test, dedicated DB integration
(presence/deadlines, concave notch, moving inside, renewals, legacy claim) and
distributed queue concurrency/recovery passed. API typecheck/build succeeded;
build.ps1 staged/deployed V34,785 verified files. Installed city descriptor
matches source byte-equivalent zone. Server HTTP state1768active/20pending/
1746checked/2outside/0deferred; neither outside trader appears in queue listing.

Stopped v33 owner8400/game14052 normally at final Ueban recheck for deployment.
That routef3cc41b2 captured88 exact snapshots:84current and4superseded after
later valid observations (not rejected). Types1/3/8 all accepted.
V34 owner17312/game14980 restarted normally; rediscover current processes.
Actual new cycle verification pending. No second reader; no purchases.

V34 restart stopped after3 bounded center preparation failures: pawn saved
at83591,147199,-3409 against Giran_V_Plaza_Wall03. Native trace starts in
0.9-unit penetration with horizontal +X normal and time0, so even outward
movements were blocked. Source V35 adds a bounded outward departure only for
initial penetration<=2, horizontal normal,30..100-unit move aligned at least
0.8 with outward normal, and a clear followup native sweep starting12units
outside initial contact. No coordinate write, hard exclusions checked first.
65 tests passed; V35 build/durable deployment verified787files. Owner6168/
game7572 restarted. Actual pawn moved from83591,147199 to83653,147170 after
bounded outward departure; route860e04dec2094a7cad1a6446dd8b55c5 then
completed18.942s,48commands,2192units,final82022,147951 inside center500,
no stop drift at11:55:03. Broker255a31605bc64ebaad40cde6f7b50594 began11:55:15. Zone is active
on server/map, and both excluded traders remain active/PENDING,attempt0,
lastErrorNULL with no visits/deferred counts. No stable whole-market claim.

Next: observe initial center/broker and claim targets excluding outside traders;
then full price-pass exhaustion, center, immediate broker and subsequent pass.
Do not call the entire market stable from policy tests or package publication.
