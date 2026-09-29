# Calculated center and radius200 — 2026-09-27

User reported incomplete central coverage and requested a calculated center,
then explicitly chose radius200. The former center(82413.6185,148116.9786)
with radius500 allowed a farthest observer-to-polygon distance3245.849,
exceeding the3000-unit radar. This possible gap does not prove any previous
retirement was incorrect.

`tools/WorldGeometry/calculate_center.py` computes the minimum enclosing circle
of all approved polygon vertices including the northeast extension. Result:
center(82710,148425), covering radius2501.784, standing radius200,
worst observer distance2701.784, remaining radar range298.216. Geometry tests
check all vertices from36 standing-circle positions. The calculated point is
walkable in the existing survey; sampled destinations have valid routes.

The saved Gamma/Giran point was updated under LocalAppData with a private backup,
preserving account/template settings. Arrival, route sampling, radar classification,
closure and independent radar proof now use the supplied center radius. UI map
receives the same200. Both collector and server require100 units of radar margin.
Server migration20260927104000 moves only the original trusted center.

LocalCycle, GiranCenter and RadarLifecycle smokes pass, including a201-unit
observer valid in500 but rejected in200. Two geometry and four planner tests
pass. Server predeploy unit/typecheck passed; build initially failed because
the disk was full. After cleanup the server build and49 integration tests pass.
Root build passes, BuildId f4f5a4eb405b4f298b4522c7405cebea. Both actual portable
packages and PS5.1 verification pass. Server ddbc78a passed CI36331206678 and
production deployment. Collector1244 (birth16:02:24.5919906Z) launched owned
game18920 (birth16:02:28.058182Z). Reader attached automatically, collection
started16:02:58 UTC. Arrived inside center200 at16:03:41 and began broker
immediately. Broker6bc7b971fa8d449591b50d5140391e41 produced a fresh complete
native_radar at16:05:37 UTC:4072 actors,1924 identities, observer
(82547.609846,148402.876340),164 units from center. Both CapitanMORGAN and
xawkNagiBator are present at the approved corner positions.43 absent historical
traders were retired at16:05:38; server history returns all43 inactive, plus
HoleyMole/Lecsi still inactive. New route d644d3ca5d2c4a0794c02b2bc14428b0 had
185 exact captures/964 rows,180 price ACKs, outbox0 and no ERROR at audit.
Some captures need server-generation reconciliation; Nox1 was unavailable and
gets one bounded end-of-pass retry. These are not proof of completion. Full
pass and corner exact read/ACK remain under heartbeat monitoring.
Collector23520/game24088
were closed by the agent for this authorized update, not manually by the user.
