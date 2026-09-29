# Giran

`city.json` selects this city's navigation geometry, coordinate origin,
validated pawn capsule, projection filters and three temple gate windows.
`giran-navigation-2026-09-26-expanded.json` uses the full saved scene extent
X 79900..84600, Y 146500..150900, including the previously excluded northern
and southern traders. It contains 329 obstacle entries and 52,156 floor samples;
10,174 missing samples remain unknown. Rebuild offline with
`tools/WorldGeometry/rebuild_giran_map.py`. The 2026-09-24 map is historical.
The expanded map preserves the captured obstacle sections,
capsule bands and ground grid. Native collision checks still guard movement.

The scene, contour data, HTML map and historical walk/course reports in this
directory are Giran-only research artifacts. Shop coordinates in those captures
are historical; current price targets and presence come from the live broker
and server queue. Routes are planned for the current position and targets.

Live gate checks on 2026-09-26 are recorded in
`docs/analysis/market-cycle/TEMPLE_20260926.md`.
## Confirmed forbidden areas

`city.json` hardExclusions contains Manor_Giran2, bounds X81850..82166,
Y149574..149802. Native mesh footprint is X81865.72..82150.97,
Y149588.77..149787.19; 15-unit padding covers the ramp/canopy and capsule.
User confirmed the booth/cart trap on 2026-09-26. All production and test
navigation retains this exclusion, including ordinary probes, relaxed room
projections and departures. Planning adds clearance; escape can leave that
additional margin without entering the padded forbidden area. Removing a
survey obstacle does not remove a confirmed hard exclusion.

## Approved collection zone

Calculated center(82710,148425), standing radius200. The polygon stays within
2701.784 from any point in that disk, leaving298.216 of the3000-unit radar.
See `docs/analysis/collection-live/CENTER_COVERAGE.md`.

User approved `city.json` collectionZone revision `giran-2026-09-27-northeast`:
X/Y polygon (80640,147430),(83590,147430),(83590,147020),(84780,147020),
(84780,147900),(84560,147900),(84560,149830),(80640,149830).
The 2026-09-27 extension adds 220 units east only above Y147900, admitting
CapitanMORGAN (84693,147700) and xawkNagiBator (84662,147581) from the live
radar capture. The remaining contour retains the previous approved boundary.
Boundary points are included. Outside shops are ignored for
price approaches/radar discovery, without deleting broker presence or prices.
Changing quantities/reopening outside does not override this coordinate rule;
moving inside makes the same trader eligible again. This is a collection
boundary, not a wall: movement may use a necessary connector outside it.

The production Market repository `C:\PriceCheck\market` applies the matching
polygon in `apps/api/src/ingest/local-cycle.policy.ts`. It does not read the
collector descriptor at runtime. The experimental `C:\broker-server` is not
the site's ingestion service.

Both new targets lie just beyond the saved navigation survey's eastern edge.
Offline planning from (84400,147700) produces a bounded ordinary movement
probe ending 68 units from each target. It does not invent floor samples;
native collision checks remain active. Live approach acceptance is pending.
