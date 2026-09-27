# Radar return and missed shop windows — 2026-09-26

V22 collector 4308/game 10196 began after successful temple-only tests.
Center route a83d427fb2ba4db5b110d1001dd588e2 completed with zero recoveries.
Broker aced59805fee456a970f0040c35d3c6d was accepted complete by PostgreSQL:
1670 traders/8336 aggregate item rows. Raw responses were 1860/1860.

First 500-target price route 1d5be04c57c5483f9582986a823f987f completed in
178.926 seconds with zero recoveries and a bounded revisit. All 214 exact shop
snapshots were current in PostgreSQL: 166 normal sells/824 rows, 17 packages/73
rows, 31 buys/150 rows. It reported Entombment (81728,146766) with no clear pass
and 42 missed anchor windows. Every one of those 42 had a live matching actor
in an in-radius scan. Reader selection favored the nearest neighboring shop;
this was scheduler starvation, not missing memory or unsupported kiosk format.

Next 499-target route 41dd31846ce1401e9d4fbd645d00886b aborted after an optional
radar detour to SoloPoke. Detour itself completed, but its endpoint
81967.241,149113.407 was inside the main route's 45-unit anchor exclusion for
SoulOfSE (19 units away). Rejoin rejected every connector and C# postponed
495 untouched targets. This caused the user's 541-deferred spike. It did NOT
mean 495 individual approaches had failed. Old output also reported the stale
pre-detour position; corrected that diagnostic.

Stopped the cycle through its own STOP file, retained/uploaded 33 exact captures
from the subsequent route 0aa600aa71af44cf997834d105058ed2, then closed owner/game.

## V24 changes

- Check rejoin before taking a radar detour. If its endpoint cannot reconnect,
  include its reversed approach path back to the original departure point in
  the same bounded plan. Total path/time limits still apply.
- Rejoin an optional detour near the original progress (minimum=0), avoiding
  skipped main-route windows. If the actual endpoint enters main clearance,
  attempt the existing bounded, native-guarded escape before reconnecting.
- Prioritize scheduled route anchors over neighboring opportunistic reads.
  Among other candidates, estimate time to leave the radius from observed pawn
  velocity, so a departing shop is read before an approaching one.
- Dynamic detours temporarily prioritize their own target; then restore the
  main route's priorities. The same reader and ordinary request/cancel path remain.

51 geometry/reader tests passed, including a detour ending in another anchor disk
and starvation/closing-window cases. Full build.ps1 passed and publication/recovery
verified all 772 installed files. V24 collector 10840/game 12052 launched; live
validation of the fixes is pending at 10:12. Do not call this stable yet.

One-time manual requeue reset 548 active failed/pending jobs with lastError,
including already-due jobs: 495 radar rejoin, 42 missed windows, four recovery
limit, three escape failures, two too-close, one no-pass, one missing binding.
Snapshot with IDs/prior reasons is workspace/requeued-skips-20260926-v24.json.
audit-requeued-skips.ts can compare actual exact receipts since this reset.
No leases, completed fresh jobs, inactive traders or price history were cleared.

At 10:18 v24 is still on its first 500-target retry route
adbd51135f0341dea2c38300f9a29cec. 151 of the saved cohort already have new
current exact snapshots; five were legitimately cancelled after complete broker
absence. Two new radar detours (Darkness, XTrade1) were read and rejoined near
the original progress (0.64/4.53 units skipped), no mass rejoin failure so far.
Full pass/cycle verification is still pending.

The first startup return 95003338e64441f28b7ab0242851d466 stopped about 11.8
from its destination but just outside the center's 500 radius. It failed the
strict zone guard and retried successfully (48d01ed8a6f74598a460e532c76d0581).
V25 source chooses destinations within 470 to leave the follower's stopping
margin and records outside_center accurately. Tests/build passed; this small
change is staged in workspace/market-cycle-v25-center-margin-stage, not deployed
during the active v24 retry pass. Deploy after a natural verified cycle boundary.
