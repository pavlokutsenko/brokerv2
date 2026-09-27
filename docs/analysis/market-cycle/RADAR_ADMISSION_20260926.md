# Accept exact radar observations of previously known shops

2026-09-26. V44 routeb0c8b576 read22/22assigned+9native-confirmed new shops,
31wire_int64 snapshots144rows,31server target cancels,0timeouts/invalid/errors.
Server stored31, but SilverDwarf, C0Cy3a6PATbEB and AltAlt were historical only.
The server already knew their durable nicknames with older position/type and
did not apply admitNewFromRadar to an existing active trader. The next broker
then unnecessarily requested another price. Both brokers were partial, so they
correctly did not deactivate unseen shops.

## Server correction

- Full exact radar observation can admit a newer shop generation of an existing
  active nickname after movement≥20/type change, as well as a new/inactive shop.
- Must be newer than current exact price and at least as recent as the latest
  broker observation. Existing full-broker absence/newer observations remain
  ordering guards. Old snapshots cannot roll back a newer shop generation.
- A changed generation seeds its complete inventory from exact rows, updates
  identity/position/type and completes the server queue atomically; obsolete
  lease revision is replaced. BrokerObservedAt is not invented. The next broker
  supplies quantities and can remove the shop after a full valid absence.
- Same-generation exact radar price preserves broker quantities. It can complete
  an unleased known shop, but cannot clear another collector's active lease.
- Ordinary assigned prices retain queueLease revision/token guards.

3market-cycle integration tests and1distributed test passed on isolated test
databases, including same-generation stock preservation/foreign lease,
changed-generation admission/obsolete lease invalidation, older radar rejection,
subsequent broker quantities and full absence ordering. API typecheck/build passed.

Live after server update: routeab51b94b read8/8assigned+8radar=16exact100rows;
all16current on server. Center96dc1c5e completed14:02:09 and immediate broker.
Deferred0. No collector rebuild needed for this API fix. New radius3000 requested
after this observation; V45 is separately tracked in NEXT_SESSION.md.
