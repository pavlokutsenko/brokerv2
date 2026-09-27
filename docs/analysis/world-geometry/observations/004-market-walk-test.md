# Movement-only market coverage test

2026-09-24, live PID 19308, same guarded build as the geometry capture.
User requested a test script to pass most market traders, with camera-independent
directions and smooth turns, without opening shops. They also requested a durable
copy of the map; standalone HTML, compact data and original scene are now saved
under `C:/broker/maps/` with a README.

## Implementation

Entry point: `test-market-walk.ps1`; user instructions in `docs/MARKET_WALK_TEST.md`.
Research code remains under `tools/WorldGeometry`, separate from production:

- `inspect_walk_functions.py`: bounded, read-only class child-function discovery;
- `walk_client.py`: fixed-index guarded functions and a PID-scoped temporary
  instance of the existing ProcessEvent capture/command bridge;
- `walk_geometry.py`: conservative obstacles, unknown-region exclusion, A* grid
  and checked quadratic corner rounding;
- `walk_plan.py`: coverage of captured traders and return to the starting point;
- `walk_follow.py`: continuous look-ahead movement, telemetry, deviation/stall
  guards and cancellation;
- `walk_test.py`: run ownership, persistence and result reporting;
- `walk_smoke.py`, `test_market_walk.py`: bounded live control and offline tests.

No target or shop function is invoked. Existing selected target remains untouched.
The reused shop capture hook is passive; only movement, stop and camera-direction
getter calls are submitted. Bridge files are isolated under LocalAppData research
market-walk/PID, not copied into production runtime sessions. A named mutex
prevents duplicate walk processes for one PID; foreign ProcessEvent patches and
active BrokerWorker processes at launch are rejected. No client speed, position,
rotation or collision memory is overwritten.

## Function evidence

Bounded UClass Children `+0x48` / UField next `+0x28` enumeration found:

| GObjects index | Function / owner | Parameter size |
|---|---|---:|
| 248406 | Move to Location by Keyboard / ControllerPC_C | 32 |
| 248407 | Move Vector 2D to Location by Camera / ControllerPC_C | 48 |
| 248408 | Get Move Camera Direction / ControllerPC_C | 24 |
| 248419 | StopIfMove / ControllerPC_C | 8 |
| 14994 | StopMovement / Controller | 0 |

Only 248406, 248408 and 248419 are invoked. Movement accepts Location at +0
(three doubles), with Time at +24. StopIfMove receives the current pawn pointer.
Reflected locals link StopIfMove to the client's StopMove interface; live controls
below establish its observed behavior. Standard Controller.StopMovement and
camera-relative input conversion were found but not used.

Absolute world-coordinate goals avoid depending on camera orientation. Initial
camera direction was `(-0.9765, 0.2157, 0)`; a +X order moved the pawn in +X,
nearly opposite the camera. Camera remains user-controlled. Rotating the camera
continuously during a run has not yet been separately controlled/tested.

## Path and limits

Map sections are approximate and incomplete, not validated navigation. Known
obstacles plus unknown BSP areas are expanded by 55 units. Route coverage uses
125 units to trader positions; this is not the 95-unit shop interaction range.
Grid spacing 40, candidate spacing 155, adaptive quadratic rounding up to 115
units, dense route samples at most 22 units apart. Look-ahead is 125–210 units;
the client adapter rejects movement endpoints beyond 260 units. There are no
random detours, timing noise, or forced camera changes.

Movement is refreshed approximately 4–5 times per second while progress is read
every 45 ms. A blocked look-ahead segment is shortened before sending. No safe
segment, route error over 85, displacement jump over 120/sample, stationary
progress for 2.8 seconds, target change, F8, Ctrl+C or duration limit stops the
test. A full run returns to its start if it completes. Routes calculate around
94–96% coverage after rounding, roughly 42,000 world units, depending on start.

## Controls and discovered stopping defect

1. `walk-smoke-19308.json`: bounded +X order (120-unit destination), movement
   6.325 units before early stop, zero subsequent drift, selected target unchanged.
2. `20260924-200455`: 14-second streaming control, 55 commands, 1,725 units actual
   travel, near 222/1,798 captured traders, peak route error 41.18. **Failed stop
   control**: one StopIfMove call was followed by 50.60 units of motion during the
   later observation window. Do not reuse the original one-shot stopping logic.
3. Stop now repeats the ordinary stop request every 200 ms until position is
   stable within 1 unit for 750 ms, bounded at 3.5 seconds. It raises if rest
   cannot be established; cleanup still restores the owned hook.
4. `20260924-200618`: 10-second streaming retest, 39 commands, 1,214.94 units
   traveled, near 178 traders, peak route error 37.48, **stop drift 0**, selected
   target unchanged. This passed the revised stop control.

A delayed prior movement response is a hypothesis explaining the first drift;
the ordering was not instrumented at the wire. The verified result is the
observed drift and success of bounded repeated stop, not proof of that cause.

Full attempt `20260924-200817`: stopped at 63.2% / 26,474 route units after
198.3 seconds, 758 movement calls, 24,420.54 actual travel units. It passed
near **1,509/1,798 traders**, peak tracking error 43.69, stop drift zero, target
unchanged. The user assessed the movement as sufficiently smooth.

Reason was `unsafe_shortcut`, not physical stalling. The final logged sample
was 54.22 units from `Giran_CH_in_front`, just inside the 55-unit planning
margin. Stopped position became `(84016.25,148388.56,-3402)`, 57.65 units away.
Read-only reflection confirmed CharacterCapsule class CapsuleComponent,
CapsuleRadius offset `+0x544` = **9.0**, CapsuleHalfHeight `+0x540` = **23.0**,
world scale `(1,1,1)`. Radius/scale are now guarded at startup. Planning still
uses 55; execution uses `max(24, scaled radius + 15)`, hence 24 here. This
distinguishes spare cornering room from the physical capsule safety margin.

`--resume-result` verifies the old map hash, connects the current position to
the next part of the saved path, and preserves its original final destination.
Continuation `20260924-201425` started with 15,389 units remaining. It stopped
on **stationary progress (`stalled`)** after 22.69 seconds / 2,262.49 travel
units at **`(83708.135,148788.981,-3409.000)`**, near the temple frontage.
There were 74 movement calls, peak route error 41.97, zero stop drift and no
target change. Character remained at that location after cleanup for inspection;
no automatic retry or return was attempted after this genuine stall signal.

Combined main pass + continuation: **26,683.03 travel units**, near **1,554 of
1,798 captured traders (86.43%)** within 125 units. Coverage is the union of
observed trajectory segments, not the sum of two overlapping counts. The full
route/return did not complete. Summary: LocalAppData
`PriceCheckCollector/research/market-walk/19308/20260924-combined-summary.json`.

At the final stop the map section is 38.92 units from `Giran_CH_side_body2`,
while the transformed full asset bounds are only 10.8 units away; the frontage
asset bounds are 10.6 units away. Nearby floor asset VP8 tops at Z=-3432 and
capsule center Z=-3409 is consistent with its 23-unit half-height. The diagram's
approximate upper floor is -3410, so its +45 section (-3365) is above this
capsule's top (-3386). This demonstrates a material floor-model discrepancy.
The exact blocking triangle/channel/server cause remains unverified; do not
declare a specific wall confirmed from bounds alone. Next investigation should
use local ground height and the whole capsule vertical band, not keep reducing
clearance around the old drawing section.

Both attempts cleaned up their owned ProcessEvent bridge; no capture state
remained and the original prologue was checked after the final stop. The saved
standalone map opened and its trader selection worked in Chromium without errors.

Five offline tests passed: obstacle detour/rounded clearance/unknown exclusion,
segment-based coverage, no progress jump at a later route crossing, and cancel
without movement, plus the planning-vs-execution margin regression. Isolated `build.ps1 -SkipBroker -OutputDirectory
workspace/world-geometry-walk-build` passed. Production files were not deployed.
