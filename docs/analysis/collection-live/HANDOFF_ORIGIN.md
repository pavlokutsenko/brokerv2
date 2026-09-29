# Moving handoff origin — 2026-09-28

## Evidence

The user's slow 9c9d pass had 61 exact reads in 20:29–20:41 UTC and frequent
individual revisits. This was not a multi-second exact-response delay: successful
native replies were usually about 0.1–0.2 seconds. In section
`route-4cce176138ea4b3f9f753634c2f87f52`, moving preparation yielded at
20:31:30.491119 from (82331.043,149328.135). The accepted route was recorded at
20:31:30.804253. The pawn continued toward the old goal during acceptance and
the next follower immediately failed `route_deviation` at 20:31:31.729461,
progress 0, error 106.55, position (82383.707,149398.324). Its six anchors then
required separate revisits. Several other sections also failed at progress 0.

## Minimal correction

The follower records the observed position/time only when it deliberately
hands ordinary movement to the next route without Stop. Before the next follow,
`walk_handoff.reconnect_handoff` connects the latest actual position back to the
validated path origin. Every original point remains, so this correction cannot
drop a passing arc, unread shop, gate or room. It does not replay a native command.

The witness must be at most 2.5 seconds old; origin drift must be 35–260 units,
and displacement from the witness must fit the existing speed/jump envelope.
The mapped connector must remain clear and at most 600 units. Invalid witnesses,
large jumps, blocked connectors and long detours keep the previous deviation
behavior. Each issued ordinary move still passes the existing capsule,
cancellation, selected-target, local endpoint and ProcessEvent guards. Follower
Stop/recovery clears the witness; its maximum age also bounds reuse between
calls. No DLL, offset or native guard changed.

## Verification

158 source navigation tests pass, including the new moving-origin regression,
stale/jump/bounds/blocked-map rejection and actual follower cancellation/capsule
checks. The old radar-only runner fixture was updated to implement the current
continuous-session client/reader/navigation interfaces, with no production
behavior change. 17 packaged module checks pass; five affected production module
hashes match source. Canonical build.ps1 passed for
`ab18a7622197426caef56749e8ca91d6`, rotation-recovery-live-handoff-origin.

Managed rotation recovery/countdown acceptance is in ROTATION_RECOVERY.md.
8125's live first session already had 66 exact reads/65 current ACK/1 historical
ACK at 21:33:39, one installed reader reused across sections. Its center capture
21:30:21 had 2186 identities/1944 zone traders, all present on the server.
This is separate evidence from the new reconnect behavior. Matching live
`handoff_origin_reconnected`, continued mapped moves and subsequent exact/current
ACK are required before claiming the new delay correction accepted. The entire
seven-character circle is still pending; do not stop at a finite pass.

Installed ab18: 860 files verified/66 replaced, own generated staging removed.
The agent drained/closed 12196/9116 for this measured correction, preserving
181 exact reads, 179 current ACK and 2 historical ACK with no pending price ACK;
the interrupted slot is not a natural completed rotation interval. Its private
upload worker was also stopped by exact PID/birth after owner closure to permit
publication; durable outbox recovery remains unchanged.

New owner 16696 birth 21:42:16.796549 UTC, game 19660 birth
21:42:21.074298 UTC, actual collection start 21:42:50.386852 UTC and fresh world
21:43:12.216745 are logged. Resumed saved slot4. Watch state carries the current
generation and the preserved circle ledger; these PIDs must be revalidated.

## Live correction accepted; full circle remains pending

The first ab18 section `route-f58866deaff94058b0ffdf12fd7343c0` records
`handoff_origin_reconnected` at 21:45:43.317479 UTC: drift87.75 units,
witness age0.763s, clear connector87.75 units. Actual next guarded move was
21:45:44.001985. DEALERYOURHAPPY exact read followed21:45:44.959270,
with current server ACK21:45:45.173463. This section did not produce the old
progress-zero route_deviation; a subsequent legitimate new-shop radar detour
was read and kept the guarded unread continuation. Private proof:
`ab18-handoff-acceptance.json`.

At21:46:57:38 exact reads/38 distinct traders/149 rows/38 current ACK,
zero historical receipts or pending price ACK. One reader installed and one
section reuse. Broker backlog1276 with zero retry attempts was still draining;
do not mistake this for delayed exact-price delivery. Fresh independent center
capture was accepted by the collector21:45:36,2161 native identities,27 retired.
Whole server retirement/outbox drain and the full seven-character rotation
remain under the active watcher. No claim that every physical stall is gone.
