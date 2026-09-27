# Shop reading during the smooth market walk

2026-09-24, requested after the obstacle-course validation. User explicitly
warns that a second target action makes the character approach the trader.
Scope: add nearby shop reads to the isolated walk test, preserve its smooth
route, validate in the live client. No transactions or coordinate spoofing.

Existing manual-passby collector already separates incoming 0x72 (replaces
movement with a trader approach) from the shop response. It temporarily skips
0x72 and avoids target cancellation between reads because cancellation stops
movement. New integration must keep that distinction and use current distances
before BOTH ordinary target actions, not the distance in an old actor snapshot.

Read: AGENTS, PRICE_VERIFIER, RESUME, manual_passby_collector, event_shop_cycle,
target controller and capture ring. Current client 19308 matches the guarded
build. Incoming table RVA 0x7EC75F0: 0x72 -> 0x4C0DA60,
0xA1 -> 0x4C10BB0, 0xB4 -> 0x4C0F760. No same-PID sender metadata exists;
the existing read-only resolvers are preparing PID-scoped caches in LocalAppData.

Implementation plan: isolated owned sender and guarded route-response
suppression; independent bounded reader loop so shop responses do not block
movement updates; fresh actor token/position validation, range margin, dedupe
by normalized nickname and shop generation, bounded failures; durable JSONL;
drain and restore all owned state at shutdown. Preserve movement-only mode.

Known capture limitation: ProcessEvent shop prices are narrowed to int32 by the
client. Sell opcode A1 has a separately validated 84-byte wire layout with
64-bit price/quantity. Do not silently label narrowed values exact wire prices.
## Controls and rejected first integration (2026-09-25)

All logs are under LocalAppData/PriceCheckCollector/research/market-walk/19308.
Stationary `130127` found no traders within 85: no requests sent, no player
motion. The initial global 0x72 suppression also counted ambient actors and was
narrowed to the moving player's ObjectID at native message +0x10; other actors
now execute the original handler unchanged. Current table 0x72 signature/RVA
is checked before install, original pointer verified after restore.

`130228`: 40s, 110 shops / 551 lots, no recovery. This is NOT smoothness proof.
`130423`: user reported jerks. Stopped at 108.9s, 302 shops / 1539 lots,
75 timeouts. Samples show 66 backwards arc steps >3, versus six over the old
307s movement-only run. Background actor scans alone do not explain this.

Added a read-only RX filter for A1 and packets whose first ObjectID is our
player's. Guarded receive RVA 0x4C1D77C chains the already-installed, validated
radar observer; no outgoing movement/coordinate packets are rewritten.
`130757` shows additional incoming 0x2F movement destinations after action
pairs, beyond the suppressed 0x72. Received 0x2F (29 bytes) contains moving ID,
origin xyz, destination xyz. 0x72 (37 bytes) contains ID, target ID, distance,
origin xyz, target xyz. Client prediction may be substantially ahead of server.

`131118`: added radius checks against both local and last observed server
origin (.8s max observation age), paced requests instead of bursts. All 45
shops answered; no backwards arc steps, but server movement still paused after
actions, so positions diverged and the server corrected the client by ~200 units.
The walk stopped on its 260-unit endpoint guard. Do not suppress corrections.

Fix: discovery/replies stay on the worker thread; the movement owner sends one
short action pair immediately BEFORE its ordinary movement command. A lock
keeps one transport owner; the movement loop never waits for a shop response.
At most one pair per .6s; fresh identity/range checks before both actions.
`131331`: 45s, 48/48 shops, 272 lots, zero backwards steps, zero recovery,
mean sampled speed 123.07 versus 122.84 in the old walk-only run. User: better.
`131523`: 55 shops/316 lots over 53s, zero timeout/recovery; intentionally stopped
to implement the user's additional requirement to cancel the target on server.

## Explicit server cancellation

The user requires clearing the server target too. The sequence is now ordinary
target action, second action, bodyless 0x48 target cancel, then the ordinary
movement command. Cancel is also sent after a partially completed pair.
`131704`: 26 shops/130 lots, 27 cancellations (one partial pair), 27 incoming
0x24 responses, final selected pointer zero, no backwards arc steps. However,
sampled speed dropped to 109.68 and late movement caused a 50.6-unit stop drift.

Read-only follow-up of 0x24: table handler RVA 0x4C12E40 -> receiver virtual
+0x110 / RVA 0x4C27CA0 -> actor event thunk 0x4B71050. Cached FName at
RVA 0x823FD68 is 0x112BE, **TargetUnselect_Order**. This confirms the cancel
response rather than a guessed opcode name. It is forwarded unchanged.
The RX observer now counts those own-player replies; the follower forces a
fresh route update immediately on each reply instead of waiting for its normal
cadence. Stop requires 1.5s of observed rest in shop mode. Validation ongoing.

`132201` validates the reply-triggered update: 35s movement, 34/34 shops,
187 lots, 34 cancels/34 server replies, final selected pointer zero, no backwards
arc steps, no recovery, zero stop drift. Sampled mean speed 123.19 and stationary
sample fraction 2.89%, close to the old movement-only control's 122.84 / 2.74%.
Full-circle validation `132324` completed: 356.545s, 40,909 units traveled,
286 shops / 1,554 lots (230 normal sell shops with exact int64 wire fields,
56 other layouts explicitly unverified). 575 actions, 288 cancellations and
288 server cancellation replies; two timeouts, eight range skips, zero invalid
replies. Three bounded recoveries: clearance, NPC, stalled waypoint. 1,065 native
capsule checks; 1,628/1,798 snapshot traders within 125 units of the path.
Returned ~2.9 units from start; final selected pointer zero, zero stop drift,
owned hooks restored. User accepted smoothness and requested individual passes;
continue with [observation 007](007-single-target-passby.md).
