# Character rotation — 2026-09-26

The user stopped market collection and requested a new UI feature: start with
character0, rotate all occupied slots, close/relaunch the client each time.
All participating characters are intended to be in Giran. Do not resume market
collection merely to test this feature.

## Settings and ownership

LAUNCH → PROFILE has Rotate characters, Interval(min), Random ±(min).
Defaults: off,60minutes,15minutes. Each successful login/reader restore draws a
fresh uniform interval45–75minutes. Repeated UI ticks do not move the deadline.
Settings are saved with the profile under LocalAppData by the existing durable
DPAPI profile store. The actual roster is runtime-only and rediscovered at login.
Slots are labeled from0. An account with one character has no needless restarts.

A normal launch with rotation enabled starts at0. Enabling it on an already
owned live client restarts at0 and discovers the roster. Existing external
clients are not taken over or killed. Before a scheduled change the application
detaches/drains collection, releases its old hooks/leases, stops the owned client,
waits for that exact process generation to exit, then logs in with the next slot.
Only the previously attached reader and enabled collection are restored. A
stopped collector remains stopped. A failed transition is latched, not retried
forever. Profile/PID ownership and city center are retained across characters.

This clock does not change market scheduling: no broker interval, minimum wait
or center deadline. A character change can cancel a current pass gracefully;
server queue truth preserves unread work for the replacement reader.

## Native roster

The supported PE build remains timestamp0x956E0D97/image0xDCEB000.
Bounded reflection discovery confirmed:

- GObjects19208: LU4LobbyHUD.LobbyCharacters, parameter size16.
- GObjects19214: LU4LobbyMode.LobbyCharacters, parameter size16.
- First property Characters is a16-byte array at parameter offset0.
- FField in this build uses Next+0x18/Name+0x20; initial generic +0x20/+0x28
  hypotheses were disproved by the bounded read-only dump. No crash or write.

`character_roster.cpp` passively observes the existing ProcessEvent path only
while rotation login has its PID-scoped roster mapping open. It validates the
known function indices and parameter sizes, recognizes the actual Blueprint
override by the validated FName/parameter size/class ancestry, and copies only
array count. No names, account credentials or character creation/deletion calls.
Count1..7 and actual selected index are returned by a separate16-byte mapping;
the existing credential mapping is unchanged and wiped normally. Missing or
invalid roster fails closed. MinHook is the existing vendored dependency; the
hook is removed before world reading starts. Only the hook created here is removed.

The first native attempt matched only base function pointers and timed out
waiting for the list. Matching the guarded override resolved it; do not repeat
pointer-only matching. Passive roster capture and every real slot subsequently
worked, with no Fatal error.

## Verification

ModuleIsolation:45/75minute bounds, fixed deadline under polling, one-slot case,
runtime roster not persisted,0→1→2→0, no stop before reader drain, duplicate
transition prevention, failed drain preserves client and latches error, enable
on running client starts0. Existing collection ownership/drain tests pass.

UI smoke rendered the actual WPF launch view and verified checkbox/inputs bind
to the profile; `workspace/rotation-ui.png`. No new UI framework or packages.

LIVE `tests/CharacterRotation.LiveSmoke`: native roster7, actual sequence
0→1→2→3→4→5→6→0 with distinct processes and validated player coordinates,
all in Giran. Old process absent after each change. No market worker, movement,
shop requests or purchases; configuration was read, not changed by the smoke.
Log `workspace/rotation-live-v2.log`; first failed attempt `rotation-live.log`.
Scheduled45–75minute elapsed live waiting was not performed; clock behavior is
verified deterministically, the real stop/login/reader workflow was accelerated.

Native login build and ModuleIsolation/UI passed. Final ModuleIsolation passed
again after revalidation of rotation settings following reader drain. Build
`build.ps1 -SkipBroker` completed; log `workspace/v52-build-final.log`.
V52 was durably published to `release/PriceCheckCollector`:805 verified files,
16 changed installed files. At16:43:48 local2026-09-26 the installed UI opened
without launch/collect flags, ownerPID8260. No game or market worker remained.
Rediscover processes before subsequent work. Collection remains stopped by
explicit user instruction. Rotation is opt-in under Launch → Profile;
defaults60±15minutes, automatically all occupied slots starting0.

Follow-up17:18: user enabled10±2minute rotation and full market run. First elapsed
V52 switch closed/drained correctly but replacement launcher failed-1. V53
adds requested10seconds after exit, early startup failure/retry, and owned-client
crash/disconnect recovery restoring the same character and prior collection
intent. UI Auto restart defaults enabled with Auto login. See market-cycle/
V53_FULL_RUN_ROTATION_20260926.md; full run remains under observation.

## Real timer and reconnect follow-up, 2026-09-26 17:35

10±2minute saved settings exercised the natural timer: character0 human→1
GrownMan completed17:23:04. A controlled disconnect of only that game's existing
TCP connection recovered character1 at17:24:38, with reader and collection intent
restored. Mandatory10seconds AFTER old owned process exit is implemented in
LaunchModule.StopAndWaitAsync. Startup errors have bounded retries; explicit user
stop forgets recovery intent. Idle WPF close reentrancy was also reproduced/fixed.

Live collection on a dwarf exposed a human-only navigation capsule guard.
GrownMan is9×18, human9×23. V54 accepts both native-confirmed sizes, computes
actual floor correctly, preserves conservative map envelope;103geometry tests.
V54 owner19856/game16660 continued slot1 using explicit --resume-character;
normal launch starts0. Return to center completed17:36:03,22move commands,11.204s.
Full market exhaustion and subsequent cycles remain pending. Detailed artifacts:
../market-cycle/V53_FULL_RUN_ROTATION_20260926.md.

## V60 pass continuation,2026-09-26

Actual natural circle0→1→2→3→4→5→6→0 completed18:51:48. Mandatory10s after
old process exit applies before replacement login. Known roster capsule shapes:
human9×23,male dwarf9×18,female dwarf5×19; all7characters reached collection.
User clarified that next character must continue the current pass. V60 retains
CycleRun, remaining server candidates, radar pool and pass counters across
rotation/recovery; no center/broker restart until exhaustion. Old ObjectIDs clear
to0 and must rebind from current-PID radar same key/type/location before native
reads. Explicit stop/start starts a fresh pass.110geometry/module tests passed;
V60 owner18972 started19:16:48. Natural continuation live verification pending.

2026-09-26 20:02 live continuation confirmed: natural1→2 at19:28:50,
2→3 at19:50:31,3→4 at20:00:05 and automatic same-slot recovery19:35:44
all retained the same price pass, counters and pending radar destinations.
No new broker on these replacements. Daybreak fresh PID binding and accepted
exact capture19:53. V63 prevents unbound carried rows from undoing confirmed
closure and removes carried closed shops from UI pool using validated current
worker identity. Fresh positive reopening remains eligible.
