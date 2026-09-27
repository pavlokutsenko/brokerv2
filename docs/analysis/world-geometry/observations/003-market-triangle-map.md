# Market geometry and visible trader map

2026-09-24, PID 19308, PE `0x956E0D97 / 0xDCEB000`.
Requested follow-up: extend the successful stair triangle reader to other
market objects and draw the trading area with traders and obstacles.

## Capture and coverage

`capture_market.py` uses the existing LU4Memory reader, the validated world
profile and reflected properties. It reads all loaded level actor lists and
walks bounded scene-component AttachChildren trees, including Blueprint lamp
components missed by the first StaticMeshActor-only survey. Selection uses
transformed asset bounds intersecting the market rectangle, not pivot distance.
No client calls/writes, hooks, movement, targeting or deployment occurred.

Sequential snapshot: **16:38:06–16:38:08 UTC / 19:38 Kyiv**. Player position
was unchanged at capture start/end: `(82320.58, 148187.98, -3473.00)`.
107 levels, 7,292 unique actors; no actor/component discovery failures.

| Observation | Result |
|---|---:|
| Visible private traders | 1,798 |
| Ordinary sell / package sell / buy | 1,360 / 117 / 321 |
| Duplicate normalized trader names | 0 |
| Mesh components intersecting capture rectangle | 306 |
| Distinct mesh assets with exact native/aux triangle match | 139 |
| Validated component instances using those assets | 302 |
| Unique asset triangles checked | 84,676 |
| Component collision modes | 276 QueryOnly / 30 NoCollision |
| Validated components contributing to the displayed section | 93 |

Traders are class-guarded CharacterPlayer_C records with kiosk type 1/3/8,
name, object ID and current root position. Their normalized nickname is the
durable key; object ID/address are capture-local. This is a visible-actor
snapshot, not broker inventory, a global server list or an atomic world freeze.

Four assets failed the strict existing geometry guard. Two are sky spheres
with NoCollision; these are omitted from the obstacle diagram. Two query-enabled
BSP models have unequal native/aux triangle multisets and remain unresolved:

- `BspConvertedToStaticMesh_544B5DE5`: XY approximately
  `[80048..80568, 148947..149675]`, Z `[-3540..-3333]`.
- `BspConvertedToStaticMesh_CDDDC0A3`: XY approximately
  `[80067..80579, 147532..148260]`, Z `[-3537..-3330]`.

Their intersecting bounds are dashed as **unknown**, not accepted as free or
blocked space. No tolerances or weaker geometry guard were introduced.

Coverage is specifically ordinary StaticMeshComponent geometry. Landscape,
spline, instanced/hierarchical/foliage meshes, skeletal actors and NPC collision
are not decoded. The capture records skipped classes; their global counts do
not establish whether every such instance is relevant inside this rectangle.

## Diagram method and limitations

`project_market.py` works offline, using the already installed Shapely package.
The displayed extent is X `[80350,84600]`, Y `[146950,150000]`; it contains all
1,798 captured traders. Geometry is transformed from validated native triangles.
Ground and stairs are projected separately as subdued context.

Obstacle marks are **sections**, not complete XY shadow projections: a high
roof or arch should not paint the space underneath as impassable. The cutting
surface is 45 units above an explicitly approximate floor: Z=-3495 on the lower
plaza, rising linearly from X=83095 to X=83481 to Z=-3410 on the upper plaza.
This floor model is a drawing assumption based on the prior stair/elevation
capture, not a measured terrain heightfield. Interiors and other elevations
need separate treatment before navigation use.

Triangles are clipped across the slope boundaries, intersected with the section
surface and joined into polygons. Connected parts are processed separately and
unioned so overlapping solid parts do not cancel each other by parity. Open
surface intersections remain thin marks (0.65-unit buffer), not invented convex
hulls. Output coordinates are rounded to 0.1 units and simplified by 0.6 units.
The four stair partitions remain distinct; each can contain separated pieces
at this particular cut height. Such small gaps are not evidence a player fits.

The map is useful for seeing trader distribution, the monument, perimeter walls,
pillars, gates, lamp posts and stair partitions. QueryOnly and a decoded shape
do **not** establish Pawn-channel blocking, capsule clearance, walkable slope,
server movement acceptance, complete obstacle coverage or a usable route.
No production navigation or collector behavior was changed.

## Reproduction, artifacts and validation

```powershell
python tools/WorldGeometry/capture_market.py 19308 --json workspace/world-geometry/market-scene-19308.json
python tools/WorldGeometry/project_market.py workspace/world-geometry/market-scene-19308.json workspace/world-geometry/market-map-data.json
python -m py_compile tools/WorldGeometry/capture_market.py tools/WorldGeometry/market_components.py tools/WorldGeometry/project_market.py
.\build.ps1 -SkipBroker -OutputDirectory workspace/world-geometry-market-build
```

Rediscover the PID first. Capture checks PE/world/class/property sizes and reuses
the double-read, index, vtable, bounds and exact triangle guards. Asset geometry
is checked once per distinct mesh and then transformed for each instance.

Raw scene is about 4.8 MB in ignored workspace storage; compact visual data is
about 146 KB. Response fragment:
`C:/Users/Pavel/.codex/visualizations/2026/09/24/01a0d35d-6f4c-7e91-8cb1-c81f56bfa9dd/giran-market-obstacles.html`.

Chromium inspection passed at 850/736/360px: all 1,798 trader marks and 93
section objects present, no JS errors or horizontal overflow, trader/object
selection and 4x zoom verified. Wide, narrow and dark-mode preview screenshots
are in `workspace/world-geometry/market-*.png`. Python compilation and isolated
`build.ps1` passed. No runtime files were replaced.

Next step remains a bounded clear-vs-wall-vs-stair capsule-query control, plus
Pawn response validation, before this diagram can inform approach waypoints.
