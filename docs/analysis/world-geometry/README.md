# World geometry and obstacle discovery

Started 2026-09-24 at the user's request to check whether the collector can
programmatically observe meshes and obstacles around the market.

**Latest:** [individual trader passes](observations/007-single-target-passby.md)
adds `-SingleTargets`: four live corner targets, curved passes with minimum
stand-off, immediate onward movement, bounded skips and server target cancel.
The initial class-guard error was fixed. Live pass completed in 91.254s: four
shops / 22 exact sell lots, minimum approach 55.78–59.03 units, no stalls or
recoveries, all four server cancellations acknowledged. Original hooks restored.
Saved route: maps/giran-single-targets-2026-09-25.png. Tests/build passed.

Previous: [shop reads during walking](observations/006-walk-shops.md) adds
`-ReadShops`, dual client/server range checks and synchronized action/cancel/move.
Replies remain asynchronous; explicit server cancellation is validated. Initial
independent send loops caused jerks and were rejected. See the note for controls.

Previous: [capsule avoidance](observations/005-capsule-avoidance.md) validates
native Pawn-profile sweeps, adds capsule-band/ground-height geometry and bounded
local replanning to the movement-only test. False stair collisions and stale
actor-page caches were corrected. Final full circle (306.87 seconds, 1,614/1,798
snapshot traders) and deliberate landmark course (99.555 seconds, all 20 corners)
completed with zero stalls and returned to their starts. Original hook restored.
Production collection remains unchanged. Saved navigation input and actual paths
are under maps/. See observation 005 for limitations and intermediate failures.

Previous: [movement-only test](observations/004-market-walk-test.md) implements
`test-market-walk.ps1`. User confirmed smooth movement; two connected attempts
passed near 1,554/1,798 captured traders and stopped near the temple. The
approximate section floor is too high at that stop; exact blocking geometry
needs ground-height / capsule-band investigation. Native hook restored, player
left at the stop for inspection. No production route changes.

Previous: [market triangle map](observations/003-market-triangle-map.md):
1,798 visible traders, 139 exactly cross-checked mesh assets / 302 component
instances, including Blueprint lamps. The map shows 93 mesh sections near an
approximate walking height; two unresolved BSP models are marked unknown.
This is not a validated navigation or complete collision map. Diagnostics:
`capture_market.py`, `market_components.py`, `project_market.py` in
`tools/WorldGeometry/`. No working collector changes.

Previous: [triangle geometry](observations/002-stair-triangle-geometry.md)
successfully resolves the stair partitions. The staircase has 561 native
vertices / 842 triangles, exactly matched to a second BodySetup representation.
Four transverse partitions belong to the same mesh as the steps. Current
diagnostic: `tools/WorldGeometry/read_mesh_triangles.py`. No client writes or
actions were needed. Earlier statements that triangles were not decoded refer
to the initial bounds-only inspection.

Original inspection scope: read-only inspection of existing code, saved metadata, and loaded world
objects through the established LU4Memory reader. No movement, target change,
new hooks, collision changes, or production route changes.

Initial finding: the working radar/queue models contain trader positions, not
an obstacle map. `native_client_move.py` invokes the client's `Move to Location
by Keyboard` function and watches progress. Calling that function alone does
not establish that a usable navigation mesh or obstacle map is exposed.

## Result: meshes and collision metadata are readable

Live PID **19308**, PE timestamp `0x956E0D97`, image size `0xDCEB000`.
Read access through the existing LU4Memory device passed a PE/world/player
smoke check. No direct ReadProcessMemory fallback, CE, new hooks, ProcessEvent
calls, target selection, movement, or client writes were used.

The validated world had **107 loaded levels**, **7,204 actor slots**, and
**2,011 StaticMeshActor instances**. Only 17 of these mesh actors belonged to
the persistent level. The existing trader actor scanner reads that one level;
the receive radar has no mesh/obstacle model. Most static geometry therefore
needs the loaded-level enumeration, not more trader packet decoding.

Examples read from the active Giran scene:

- `Giran_V_Plaza_Stair01` and `Giran_V_Plaza_Elevation`;
- `Giran_V_Plaza_Pole*`;
- `Giran_V_Plaza_Wall*`, `Giran_V_Market_Wall*`, `Giran_Tree_Fence*`;
- `KnightStatue_S*`.

For **288 mesh instances whose root position was within 3,000 XY units** of
the player, reflected metadata produced zero parsing failures:

| Readable property | Observation |
|---|---|
| Mesh asset / component | Named StaticMeshActor -> StaticMeshComponent -> StaticMesh chain |
| Position | Root cached position matched reflected RelativeLocation for all 288 sampled roots |
| Asset bounds | Reflected ExtendedBounds, 56 bytes; local-space bounds, not an obstacle polygon |
| BodySetup / BodyInstance | Both resolved with class / reflected property-size guards |
| CollisionEnabled | 271 `QueryOnly`, 17 `NoCollision`; enum meanings decoded from this client's UEnum |
| CollisionTraceFlag | 286 `CTF_UseComplexAsSimple`, 2 `CTF_UseDefault` |
| CollisionProfileName | 275 `Custom`, 13 `BlockAll`; profile alone does not establish Pawn-channel response |
| Simple collision shapes | All sampled AggGeom shape arrays empty; this does not mean no collision |
| NavCollision | A NavCollision object on all 288 assets; no usable world path graph validated |

The root-distance filter can omit a large nearby mesh whose pivot is farther
away. These are a bounded sample and sequential observations, not a complete
atomic obstacle map. At that initial stage, mesh triangles, cooked complex
collision, world-space footprints, Pawn response channels, and surface
walkability had not been decoded; the triangle follow-ups are linked above.
Do not mark an entire mesh bounds box as blocked: stairs and ground meshes are
also in this collection, and asset transforms still matter.

## Collision query route and navigation limits

GObjects index **4723** resolves live to `KismetSystemLibrary` /
`CapsuleTraceSingleForObjects`, class `Function`, parameter size **401**. The
old independent collector has a prior implementation at
`C:/PriceCheck/collector/native/PriceCheck.Native/src/world_collision_query.cpp`
and historical blocking-column observations. It was inspected read-only as a
reference; no dependency or code copy was introduced into the working route.

The function's existence and layout are confirmed in the current client;
**a fresh clear-vs-wall trace control has not been run**. No `RecastNavMesh` or
navigation actor appeared in the enumerated level actor classes. This does not
prove absence of every native navigation structure. Per-asset NavCollision is
not evidence of a ready-to-use pathfinding graph.

The current movement function at index **248406** also retained the expected
name, owner `ControllerPC_C`, and 32-byte parameters. Its invocation delegates
movement to the client; that alone does not prove reliable obstacle avoidance.

Next useful experiment: validate the reflected capsule-query parameters and
execution route, then compare a known clear segment, a segment through a wall
or pillar, and a stair segment, without moving the player. Only after controls
pass should query results guide approach waypoints and deferred-trader policy.

## Reproduction and artifacts

Diagnostics are separate from production and use build/runtime guards:

```powershell
python tools/WorldGeometry/inspect_world.py 19308 --json workspace/world-geometry/loaded-world-19308.json
python tools/WorldGeometry/inspect_collision.py --world-json workspace/world-geometry/loaded-world-19308.json --json workspace/world-geometry/collision-metadata-19308.json
.\build.ps1 -SkipBroker -OutputDirectory workspace/world-geometry-build
```

Rediscover the PID before rerunning; recapture the world before its metadata.
The two live diagnostics and the isolated build passed. Nothing was deployed.
Raw reports are in ignored `workspace/world-geometry/`, including the initial
world probe, property metadata, and UEnum decoding.

Verified discovery offsets for this build: GWorld RVA `0x81F6A80`, GObjects
RVA `0x80768E0`, FNamePool RVA `0x7FBFB80`; UWorld loaded Levels TArray `+0x178`,
ULevel Actors `+0xA0`, actor root `+0x1A0`. UStruct super `+0x40`, child fields
`+0x50`; FField next `+0x18`, name `+0x20`, property size `+0x34`, offset
`+0x44`, StructProperty/ByteProperty type `+0x70`. The first exploratory pass
using next `+0x20`, name `+0x28`, offset `+0x4C` produced invalid names/sizes;
that layout was rejected and must not be reused. Production promotion would
require fixed validated property offsets rather than discovery enumeration.

## Schematic visualization

Follow-up: the user's screenshot exposed initially unresolved stair partitions and
mesh-bearing Blueprint actors omitted by the StaticMeshActor-only view. See
[stair partitions](observations/001-stair-partitions.md). Four nearby lamp
components were identified; subsequent [triangle extraction](observations/002-stair-triangle-geometry.md)
resolved four stone partitions inside the stair mesh. The original schematic
remains a partial bounds view, not a collision map.

At the user's subsequent request, a top-down schematic was drawn from these
captured instances. An additional read-only pass validated 288 root cached
transforms: quaternion at component `+0x1D0` had unit norm, position `+0x1F0`
matched the captured position, and scale `+0x210` matched reflected
RelativeScale3D. Reflected RelativeRotation and scale were also saved.

Each asset-local ExtendedBounds box was scaled, quaternion-rotated and
translated. The XY convex hull of its eight transformed corners provides the
displayed conservative bounds projection. This is not a triangle outline or
walkability result. The view shows 114 intersecting sampled mesh bounds around
the plaza, the prior captured player location, and a 95-unit reference circle.
It does not contain trader markers or a generated route.

Raw visualization inputs: `workspace/world-geometry/map-data-19308.json` and
`map-compact.json`. Interactive response fragment:
`C:/Users/Pavel/.codex/visualizations/2026/09/24/01a0d35d-6f4c-7e91-8cb1-c81f56bfa9dd/giran-programmatic-map.html`.
The static-scene view and object selection passed Chromium inspection at
850px and 360px viewport widths, with no JavaScript errors. Screenshots and
the temporary standalone preview are under `workspace/world-geometry/`.
