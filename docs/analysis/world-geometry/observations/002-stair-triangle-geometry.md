# Stair triangle geometry and four transverse partitions

2026-09-24, PID 19308, current build `0x956E0D97 / 0xDCEB000`.
The user explicitly asked to try reading the inner geometry. This follow-up
resolves the unidentified partitions in observation 001.

## Verified result

`Giran_V_Plaza_Stair01` contains four narrow transverse wall-shaped parts in
the **same mesh** as the steps. They disappeared in the previous schematic
because it drew one box around the whole mesh.

The BodySetup exposes two readable indexed representations. The native object
has **561 vertices / 842 triangles**; the auxiliary representation has **1,440
vertices / 2,526 indices**. Their triangle coordinate multisets match **exactly**
(no rounding or tolerance), after ignoring face order and winding and preserving
multiplicity. Vertex deduplication differs. Both arrays were read twice and
were stable, all indices were in range, coordinates finite, and triangle bounds
fit the reflected asset bounds.

| Control asset | Native vertices | Triangles | Exact auxiliary geometry match |
|---|---:|---:|---|
| Giran_V_Plaza_Stair01 | 561 | 842 | Yes |
| Giran_V_Plaza_Elevation | 44 | 40 | Yes |
| Giran_AdenaGirl_Btm | 184 | 248 | Yes |
| Giran_V_Plaza_Pole6 | 40 | 68 | Yes |

Welding equal vertex positions and grouping shared triangle connectivity gives
11 components for the staircase: five stepped portions, four transverse
partitions, and two small end portions. Each partition has 70 triangles and
world bounds about **149.36 X x 36.06 Y x 96.36 Z** units. The Z span is the
whole part's bounds, not its clearance above each adjacent step.

| Partition | Centre X | Centre Y | World Z bounds |
|---|---:|---:|---|
| 1 | 83169.33 | 147912.77 | -3495.09 .. -3398.74 |
| 2 | 83169.33 | 148395.22 | -3495.09 .. -3398.74 |
| 3 | 83169.33 | 148842.57 | -3495.09 .. -3398.74 |
| 4 | 83169.33 | 149322.95 | -3495.09 .. -3398.74 |

The two middle Y centres match the previously read nearby lamp positions
148394.67 / 148842.74. The extracted shape and repeated arrangement agree with
the screenshot's partitions. This is geometry evidence, not a successful
character traversal test or proof of server movement behaviour.

## Layout and safeguards

For this build only:

- StaticMesh BodySetup is resolved through the reflected property; class guard
  `BodySetup`, reflected `BuildScale3D` must be exactly (1,1,1).
- BodySetup `+0xF8`: native object reference array; exactly one object on these
  controls. Native object's vtable must equal module base + `0x63E7A58`.
- Native object `+0x48`: TArray of 12-byte XYZ float32 vertices.
- Native object `+0x70`: TArray of 6-byte triangles (three uint16 indices).
- BodySetup `+0x108`: auxiliary TArray of int32 indices.
- BodySetup `+0x118`: auxiliary TArray of 24-byte XYZ float64 vertices.
- Native vertices interpreted as float64 and indices interpreted as uint32
  failed finite/range controls. Those formats were rejected.
- A preliminary RTTI guess at vtable[-1] failed with a partial read; it points
  to code rather than a usable RTTI descriptor here. No C++ type name was
  inferred from that failed probe. The reader uses the verified vtable guard
  and cross-representation geometry checks instead.

The reader validates PE identity, world identity, mesh/component classes,
array bounds and repeat-read stability. It does not claim support for other
builds, multiple native objects, uint32 native indices, or scaled BodySetup
geometry. No native/DLL changes, client calls, writes, target changes, movement,
hooks, or deployment were performed.

## Reproduction and display

```powershell
python tools/WorldGeometry/read_mesh_triangles.py --world-json workspace/world-geometry/loaded-world-19308.json --json workspace/world-geometry/validated-triangle-meshes-19308.json
.\build.ps1 -SkipBroker -OutputDirectory workspace/world-geometry-triangles-build
```

Rediscover PID and recapture world metadata after a client restart. The reader
and isolated build passed. Helpers: `tools/WorldGeometry/triangle_geometry.py`.
Raw probes, rejected formats and four control reports remain under
`workspace/world-geometry/`. Main verified data:
`validated-triangle-meshes-19308.json`.

The response `giran-stair-geometry.html` in the thread visualization directory
shows actual triangles, with four partitions highlighted and an additional
40-triangle Elevation mesh for context. The upper view exaggerates Z by 3x;
the top view preserves XY proportions. Object selection and all 882 faces
passed Chromium checks at 850px and 360px with zero JavaScript errors.

Next: determine the effective collision response to the character and validate
clear-vs-partition traces before using the geometry for automated approach
points. Ground, stair surfaces, wall surfaces and clearance need separate
treatment; a whole mesh's XY bounds remain unsuitable as a blocked polygon.
