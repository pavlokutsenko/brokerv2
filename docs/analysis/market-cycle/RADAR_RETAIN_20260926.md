# Keep a newly admitted shop through the current price pass

2026-09-26, V44. V43 real first route2ac1c3b7 covered14/14assigned and6new;
20exact accepted82rows, center13:45:41, immediate broker. Read→move timings
.095/.115/.163s. Second1991f222 covered4/6assigned+5new,9exact accepted39rows;
Heldin/Korupcija lacked a nearby native actor after3fresh scans, releasedREADY
without Deferred/errors, center13:48:22 and immediate broker. SDRT was also
native absent (new radar, no invented server price). These did not stop market.

## New observation and policy gap

Heldin first appeared in radar at13:45:20,81195/147995, while pawn81035/147602
(~424units) followed the final individual NotForPoor approach. It was not read
in that pass; next broker assigned it, then actual nearby native scans found
no actor. V43 did not log first native admission, so it cannot establish
whether Heldin was native-confirmed before it disappeared.

Code inspection establishes a separate loss window: individual approaches
disable recursive radar detours; new targets are added between approaches.
Even a shop already native-confirmed within500 was discarded if the current
approach ended >500away or >2seconds after its last nearby observation.
This is a scheduling gap, not proof of an incomplete memory scan.

## V44

- The existing single reader records radar_live_admitted once, only after
  ObjectID/class/type/native actor match within500. Stores admitted_at.
- Such targets remain eligible after the current individual approach, regardless
  of distance/age at its end. Cache-only targets retain fresh≤2s/≤500 gating.
- Nearest pending choice, one handling per key,600s route budget, collection
  polygon, hard traps and live actor/range guards remain. Genuine absence after
  a completed approach skips the pass; no fake exact result or admission.
-85geometry tests/build.ps1,795verified durablefiles, deployed13:49. Normal
  close/relaunch, no second reader. Live evidence and current PIDs are in
  NEXT_SESSION.md. Do not claim this scheduling branch verified merely from
  Heldin; it lacked native-admission logging in V43.

## Live V44 proof and V45 radius

Routeb0c8b576: SilverDwarf added9.682s after native admission,864units away;
PPbestPP added8.646s later,1070units away. Both subsequently read in this pass.
The old≤500 filter would have discarded them. All22assigned+9native-admitted
new shops read; the tenth packet-only nameFOP111 had no native admission.
NPC capsule collision replanned promptly. South temple centerline probe entered,
interior traders read, center returnccde20b3 completed13:55:40 (no blocked exit).
Read→move median.131s/max1.341s across22handoffs; longer cases were connector
planning (.729s max), not a wait for the shop reply. 0decoder/timeouts/errors.
Three historical-only snapshots led to the separate RADAR_ADMISSION_20260926.md fix.

User then requested discovery radius3000. V45 applies it at C# radar frame,
native admission, detour choice and pending targets; center stays500/read≤95.
Giran collection polygon still filters admission. Long new-shop approaches
bounded55s/path≤6000, within the same600s operation budget; no retry timer in
the center.86geometry tests/build passed,795verified files. Live V45 proof
is recorded in NEXT_SESSION.md.
