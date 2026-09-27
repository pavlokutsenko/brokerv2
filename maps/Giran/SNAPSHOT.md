# Saved Giran market snapshot — 2026-09-24

Open `giran-market-2026-09-24.html` in a browser. It is a standalone export of
the interactive map shown in the conversation. Click a trader/object for its
name, scroll or double-click to zoom, drag to pan. No live client is needed.

- `giran-market-2026-09-24-data.json`: compact map contours, trader positions,
  section assumptions and capture counts.
- `giran-market-2026-09-24-scene.json`: original read-only scene snapshot with
  validated triangle assets, instance transforms and all capture failures.
- `giran-navigation-2026-09-24.json`: derived navigation input, adding 108
  capsule-band obstacle entries and sampled floor heights. The planner also
  derives height-discontinuity barriers from that grid. The original map is
  kept unchanged. Native collision checks and bounded recovery remain required.
- `giran-market-walk-2026-09-24.png` and matching `.json`: actual full-market
  validation path and component proximity report. 306.87 seconds, 37,523 units,
  return to start, no stalls, 1,614 / 1,798 snapshot traders within 125 units.
- `giran-obstacle-course-2026-09-24.png` and matching `.json`: deliberate loops
  around all four stair partitions and the monument with its four attached lamps.
  All 20 control corners passed; 99.555 seconds, no stalls, returned to start.

The snapshot contains 1,798 visible traders at 19:38 Kyiv. It is not a current
global trader catalogue. Obstacle sections use an approximate floor model;
dashed BSP areas are unknown. They do not establish character walkability.

See `docs/analysis/world-geometry/observations/003-market-triangle-map.md` for
capture provenance and reproducible geometry processing.
