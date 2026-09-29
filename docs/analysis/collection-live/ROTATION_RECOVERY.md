# Rotation recovery and visible countdown — 2026-09-28

## Measured failure

The old 9c9d UI owner was Collector 24532. Rotation stopped/drained its
17720 client at 20:44:13–20:44:17 UTC. Replacement 25528 was born at
20:44:29.799768 UTC. The log proves `collection started` at 20:44:58.841,
followed by `changed to character 3 of 7`; startup was actually pressed.
World data arrived at 20:45:20 and movement towards the center began.

At 20:45:37, `WalkRecovery.unstick → WalkClient.stop → invoke_process_event`
failed with `game-thread ProcessEvent command was not consumed`. Cleanup
also found `ProcessEvent command bridge is busy: trigger=1 status=0`.
The collector correctly marked the native lane unsafe and wrote STOP,
but Gamma's saved automatic restart was disabled. A later cycle retry
entered that STOP marker at 20:46:08 and treated it as a cancelled route,
clearing collection intent. This is not evidence of a manual user stop.

## Changes and boundaries

An unsafe lane now enters `Client recovery`, retains collection intent,
and cannot schedule another route while `ClientFault` is present. Existing
owned-client recovery drains and replaces this client; it never resets or
replays an armed ProcessEvent command. Three incomplete center captures or
return failures also request recovery when automatic restart is enabled.
Gamma's existing profile has automatic restart enabled. Explicit manual
Stop/F8, proxy/HWID protection failures and bounded unread-shop retries
retain their existing meanings.

The COLLECTION status panel now shows the existing rotation service's
actual deadline as `Персонаж 5 из 7 · до смены 00:29:58 · в …`. The clock
updates each second without drawing a new jitter value or changing the
30-minute interval/5-minute jitter. The profile's saved slot became 4
before the old owner closed; startup uses `--resume-character` to preserve it.

## Validation and installation

`build.ps1 -BuildLabel rotation-recovery-countdown -SkipPackages` passed.
Build `8125c33237034bbd828731f40d2e435a` was durably installed: 856 files
verified, 63 replaced. Own installation staging was removed.

`ModuleIsolation.Smoke --rotation-recovery-only` passed native fault intent,
no second unsafe worker, circular slots/stable countdown/deadline, drain,
same-slot recovery, retry backoff and manual-stop checks. Its native-failure
fixture resolves the continuous session command's `input` path, rather than
assuming every worker argument directly contains targets. The full legacy
ModuleIsolation fixture still fails its old two-Gamma-reader scenario under
the current one-market-reader guard; no production guard was weakened.
CharacterRotation.UiSmoke and CollectorUi.Smoke passed binding/rendering,
selection/deletion and unchanged real settings checks.

New Collector 12196 was born 21:27:11 UTC; owned game 9116 at 21:27:16.
Actual reader attachment 21:27:38 and collection start 21:27:46.160620 UTC
are logged. These are historical launch evidence: current PID/birth/log and
progress must always come from the private watch state and live processes.
First current-session price/read/ACK and a complete seven-character circle
were pending at installation, not claimed by the tests.

## Continued acceptance

Pavel explicitly resumed observation until a full seven-character circle.
The private watch state's `rotation_monitor` records initial slot 4 and
expected slots 4,5,6,0,1,2,3,4. Count natural completed working sessions and
verify the wrap's collection starts; same-slot recovery is not another slot.
Each slot requires actual fresh world data, center/broker/native radar, and
movement/read/current delivery when targets exist. An empty queue requires
real repeated broker/center checks, not fake reads. Never shorten rotation
to make acceptance finish faster. Save meaningful stalls and measured
phase/read timing, then make only evidence-based delay fixes.

The heartbeat must stay active through this entire circle, including new
finite passes. The previous first-finite-pass pause condition is superseded.
Stay quiet on ordinary progress. On a genuine explicit user stop, report
once and pause; agent-authorized installation is not that stop. On the
verified circle, report real counts and unresolved targets, pause monitoring,
and leave the collector working. Do not claim native physical pauses are all
fixed merely because managed recovery and countdown checks passed.
