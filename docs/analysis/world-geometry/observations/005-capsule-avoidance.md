# Capsule query controls and bounded avoidance

2026-09-24, PID 19308, build `0x956E0D97 / 0xDCEB000`.
User requested live obstacle testing and avoidance. Production collection is
unchanged; all movement uses the isolated research bridge and ordinary client
movement/stop functions. No targets, shops, coordinate writes or collision edits.

## Matched native query controls

`capsule_probe.py` validates the reflected Kismet parameter and HitResult
layouts before using the existing game-thread ProcessEvent command bridge.
`CapsuleTraceSingleForObjects` (index 4723, 401 bytes) falsely reported initial
overlap with `BM_Giran_Music / BrushComponent0` even on known clear ground.
Complex/simple switches did not fix that negative control. Do not use this
broad object-type query for navigation. ObjectTypes elements are **bytes**,
validated from ArrayProperty.Inner `+0x78`, element size `+0x34`.

`CapsuleTraceSingleByProfile` (index 4722, 393 bytes) with the native **Pawn**
profile and current CharacterPlayer_C actors ignored passed four controls:

| Control | Result |
|---|---|
| Clear plaza, `(82300,148180,-3470)` -> `(82450,148180,-3470)` | Clear |
| Monument, `(81650,148610,-3450)` -> `(82200,148610,-3450)` | Giran_AdenaGirl_Btm, hit distance 69.120 |
| Temple front, `(83700,148780,-3406)` -> `(83800,148780,-3406)` | Giran_CH_front_body, distance 39.348 |
| Escape west, `(83708,148789,-3406)` -> `(83570,148789,-3406)` | Clear |

Source/after position was identical. Player actors must be ignored because the
query otherwise reports the capsules of seated traders on paths already walked.
The ignore array is rebuilt from the current persistent-level actor array for
each query, bounded at 8192 pointers; its owned allocation is freed on cleanup.
Pawn is not asserted equivalent to every response of the player's Custom
profile. These controls validate the tested static obstacles, not all geometry.

Raw controls: `workspace/world-geometry/capsule-controls-pawn-ignore-19308.json`;
failed controls are adjacent `capsule-controls*.json` files.

## Ground and obstacle map

`ground_surface.py` reads walkable floor/stair/elevation triangles from the
saved scene. Stair wall components (70 faces) and end pieces (26) are excluded
from floor extraction. At the old stall, actual floor is **-3432**, capsule
centre **-3409**, half-height **23**, radius **9**. The original approximate
section at **-3365** was above the capsule and missed the bottom of the temple.

`build_navigation_map.py` clips non-floor obstacle triangles against local
ground +4..+50 and adds 108 capsule-band entries to the original 93. Ground
grid: 32,956 cells, 6,035 unknown. Saved as
`maps/Giran/giran-navigation-2026-09-24.json`; original map/scene remain preserved.
This is a partial geometric model, not a recovered server navmesh.

## Algorithm under live validation

Global A* grid, inflated obstacles, visibility simplification and checked curves.
Ahead of each new movement goal, `walk_guard.py` checks a native capsule sweep;
slopes are split into short segments following floor heights. A low stair contact
gets a second sweep 16 units higher, including vertical risers. It is accepted
only if that second sweep is clear; the tall partitions remain blocked. Local
height discontinuities over 28 units are blocked. Global planning also adds
barriers across adjacent 20-unit ground samples differing by more than 24.
`walk_recovery.py` stops, adds the observed contact as a bounded exclusion,
replans a curved connector and rejoins the remaining route. Repeated failures
defer a longer piece. Four attempts per 120-unit zone, 12 total; cancellation,
deadline and external target-change guards remain. Logs record every recovery.

## Live validation trail

- `203455`: 664 units, escaped old temple stall. Detected a missing elevation
  edge, ended `recovery_unreachable`. Fixed by considering vertical Elevation
  faces, ground discontinuities and a short native-checked escape from inflated
  margins (three movement attempts maximum, local deadline).
- `203804`: 468 units, false stair tread collisions, `recovery_limit`. Initial
  fix retried only upward-facing stair contacts 12 units higher.
- `203948`: 8,458 units / 70 seconds, 928 traders, one successful automatic
  detour around a CharacterCapsule contact; stopped on intended duration.
- `204128`: 17,859 units, manually cancelled with STOP after vertical stair
  risers caused repeated false obstacles. No stationary stall; but needless
  detours were unacceptable. The low-contact retry now includes vertical risers
  and uses 16 units. Replay of all **seven** failed stair segments becomes
  clear; the separate **NPC** control remains blocked. Raw controls:
  `workspace/world-geometry/stair-riser-controls-19308.json`.

All listed movement logs are under LocalAppData research/market-walk/19308 and
all ended with stop drift 0. The pre-fix `selected_unchanged` flags were based on
cached pages and are not independent evidence of a fresh target check. No target
action exists in these movement scripts. Post-fix runs validate the live pointer.
Every completed
control/run restored the ProcessEvent hook.

- `204613`: 19,131 units, one stationary event recovered, then false capsule
  blocks exhausted the 12-recovery budget. Root cause: the one-shot diagnostic
  `Memory.unpack` uses permanently cached pages. Current actor-array entries
  combined with old UObject/GObjects pages misclassified newly streamed players
  and even produced invalid actor names. Fix stays in the live research wrapper:
  clear pages at every position/world guard, actor-ignore refresh and hit decode.
  Do not remove the cache globally from snapshot diagnostics.
- Replay of all ten capsule blocks from that run is clear after refresh. Each
  flat check takes about 100 ms. An above-tread partition control still blocks
  at distance 24.415, with normal -Y, no initial overlap: `(83200,149270,-3410)`
  -> `(83200,149370,-3410)`. Raw evidence:
  `workspace/world-geometry/fresh-actor-controls-19308.json`.

## Completed full-market validation

`20260924-205122`: **completed**, 306.87 seconds, **37,523.41** actual XY units,
859 movement commands, **952** native capsule checks, **zero** recoveries/stalls.
Return position `(81374.058,149127.928,-3473.000)` is 2.75 XY units from the start.
Peak route error 38.71, stop drift 0, live selected pointer unchanged. Passed
within 125 units of **1,614 / 1,798** captured traders (**89.77%**). Counts refer
to the saved market snapshot, not the current global market or shop availability.

The logged path passes within 150 units of 27 captured components: stair
partitions, elevation edge, monument, streetlights, poles, plaza walls/gates,
temple sides/front and grocery exterior. Proximity is not all-sided validation.
Saved actual path: `maps/Giran/giran-market-walk-2026-09-24.png`; machine-readable
proximity/coverage report next to it. Exporter: `report_walk.py`.

Follow-up deliberate course `205658` **completed**: four-sided loops around all
four stair partitions and the monument. **20 / 20** checkpoint corners passed
within 100 units (the route is curved, not stop-at-every-corner). 99.555 seconds,
11,541.59 units, 244 movement commands, 358 native checks, zero recoveries/stalls.
Return within 1.92 units of start, peak route error 29.36, stop drift 0, fresh
selected pointer unchanged. Saved path/report:
`maps/Giran/giran-obstacle-course-2026-09-24.{png,json}`.

The four O_LoadLamp01 components lie
inside the monument envelope, so they are grouped with its loop; their backs
cannot be circled independently. Five landmark loops, none omitted by planning.

Both final runs use the current cache refresh and stair-step logic. There are
still unknown BSP and undecoded geometry classes; this establishes the tested
routes/landmarks, not universal navigation through every scene asset. Recovery
was exercised during intermediate live passes and bounded-failure unit tests;
the two final routes did not require it. Working collection remains unchanged.

Verification: 11 focused tests, Python compileall, isolated build via
`build.ps1 -SkipBroker -OutputDirectory workspace/world-geometry-avoidance-build`.
After the final run, the original ProcessEvent prologue matched and the owned
capture-state file was absent. Player left stopped at `(81372.178,149127.534,-3473)`.
