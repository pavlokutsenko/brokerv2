# Stair partitions missing from the schematic

Resolved in [observation 002](002-stair-triangle-geometry.md): four partitions
are connected triangle components within Giran_V_Plaza_Stair01. The uncertainty
below describes the earlier bounds/component inspection.

2026-09-24. The user pointed out transverse stone partitions on the staircase
in a new screenshot. The previous schematic does not resolve those partitions;
its green polygon is a transformed asset bounding box, not a mesh outline.

Read-only follow-up in the same PID 19308 / validated world:

- Enumerated scene components attached to non-character actors whose roots lie
  near the staircase; followed reflected AttachChildren, bounded to 64 nodes
  per actor. Raw result: `workspace/world-geometry/stair-actors-19308.json`.
- Found four nearby `GL_Lamp1_C` actors with a `LU4_4` StaticMeshComponent using
  `Giran_StLight02`. The original StaticMeshActor-only schematic omitted these
  mesh components. Two lamps have X about 83113 and Y about 148395 / 148843,
  near the lower stairs. Their mesh bounds are about 16 by 83 XY units. These
  are confirmed lamp components, **not an identification of the stone walls**.
- Checked bounds intersection for all 2,011 previously enumerated
  StaticMeshActor instances plus those four lamp meshes, removing the earlier
  3,000-unit actor-root-distance restriction. 36 bounds intersected the wider
  stair test box. This also recovered large floor/elevation meshes with far
  pivots. Raw result:
  `workspace/world-geometry/stair-overlapping-meshes-19308.json`.
- The staircase bounds remain one rectangle: X 83094.7..83503.6,
  Y 147441.2..149793.0, Z -3495.3..-3398.7. The separate Elevation bounds also
  do not reveal internal surfaces. No individually verified stone-partition
  component was identified by this bounded pass.

Do not conclude that the walls are absent from client memory or assert that
they are part of the stair asset without triangle/collision evidence. They
could be within a combined mesh or another component/instanced representation
that this inspection has not covered. The component walk was local by actor
root, not an exhaustive traversal of every component in every level.

Required next evidence: actual mesh/complex-collision surfaces or controlled
capsule/line traces across the pictured partition and the adjacent passage.
The schematic cannot establish walkability from an empty region inside its
bounding boxes. No speculative partition polygons were added to the map.
No client calls, movement, target change, new hooks, or production edits.
