# Manor cart trap and permanent exclusion — 2026-09-26

User screenshots confirmed Manor_Giran2 at the north plaza wall as a booth/cart
with ramp. Eight native departure rays hit that mesh; altered movement-goal Z
did not free the character. User manually exited and authorized collection.

The diagnostic ordinary probe had omitted every projected obstacle. Production
direct recheck fallback used the same geometry relaxation, so it could repeat
this trap. Giran city.json now owns a hard exclusion of the complete saved native
mesh footprint plus15: X81850..82166,Y149574..149802. This survives both ordinary
and room probes. Direct rechecks compute a connector around it; escape rejects
crossing it before a native probe or movement command. Other cities have no
Giran exclusion. Test temple_walk --ordinary-probe retains it as well.

First deployed V32 startup at82189,149685,-3473 was outside the cart but just
inside its extra24-unit planning clearance. Escape incorrectly checked that
inflated exclusion and prevented an outward departure; no move into the cart
occurred. Three bounded center errors stopped collection. V33 separates the
padded hard boundary from the additional planning clearance, permits outward
departure, and still rejects entry/crossing. Regression covers this exact point.

59 WorldGeometry tests passed; build.ps1 and durable publication verified781
files. Owner1700/game10752 closed; V33 owner8400/game14052 restarted using normal
launch-profile/collect-profile flags. Live center route
route-121fb52b66364bbfb4d5f280bf8b147f completed13.776s with35 commands,
1506units traveled, peak route error0.72, no stop drift, final82806,148225,-3473
inside UI center500. Departure from cart margin succeeded. Next broker
broker-3dc36a8e44a44f6e94b1938511b51460 started at11:30:37. Market results pending.
Initial departure needed3 short native-checked commands (first2 no motion);
subsequent center following required no recoveries. Native current position
really changed onthird command; not a synthetic coordinate write.

Broker finished all1873 catalog replies, raw complete=true,1677 unique traders
and1674 named in raw summary. Server ingest remains conservative and partial:
1676 traders/8342rows, no absence cleanup. Routef3cc41b2a4434f959f05879d0b510dbf
then started100 targets. At11:34 audit found19/19 current exact snapshots,
14sell60rows and5buy32rows. Automatic collection remains enabled onone owner.
This proves restart/price acceptance, not several stable complete cycles.
No second reader, no purchases, no actor coordinate writes.

Map snapshot1692 active traders includes all3 kiosk types (1291/285/116).
Reusable display-only exporter tools/WorldGeometry/trader_map_data.py produces
saved geometry/coordinate data; raw data/PNG live under workspace.
Contours are original decoded sections, not a guarantee of passable geometry.
User intends to ignore distant shops beyond walls after viewing the map;
no arbitrary radius or ignored trader identity has been applied yet.

Map script parse passed node --check; equivalent data/contours were rendered
toPNG and visually inspected with both pending outliers labelled. Local file URL
preview was blocked by browser policy; no alternate browser preview attempted.
