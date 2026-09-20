# Trader presence and range loss

LU4 `DeleteObject` means that an actor left the client's current knownlist. It
does not prove that a private shop closed. This matters when the broker/radar
character temporarily leaves the market to buy something and then returns.

The packet store now keeps two states:

- current actors, removed immediately by `DeleteObject`;
- the market trader catalog, keyed by normalized nickname and retained when the
  actor goes out of range.

A retained shop keeps its last ObjectID generation, shop type, coordinates and
last-seen time, but is marked invisible. A new supported `CharInfo` refreshes
all fields and makes it visible. A visible `CharInfo` for the same nickname with
no supported private-shop type marks the shop inactive.

The UI counter displays `known / visible`; dim radar points are retained
out-of-range shops. Durable persistence and broker-confirmed expiry belong to
the planned SQLite writer and are not implemented in this in-memory milestone.

## Confirmed absence rule

The current safe presence radius is 2,000 world units. When the character is
farther away, missing traders remain retained because they are outside the
client knownlist. When the character comes within 2,000 units of a retained
trader's last coordinates and that trader is still absent, a three-second
packet-settle interval starts. If no supported `CharInfo` arrives during that
interval, the shop is marked inactive. Moving outside the radius cancels the
pending absence decision.

The configured market center is not assumed to cover the complete market.
Traders outside the current 2,000-unit circle remain retained and unverified;
they are never expired merely because the character returned to its center.

## Planned market coverage

Presence will use three explicit states:

1. `visible`: currently present in the packet knownlist;
2. `unverified`: last known shop is outside a completed coverage circle;
3. `gone`: absent from a completed coverage circle that contains its last
   coordinates.

A coverage sample is valid only after the character stays near its checkpoint
for the packet-settle interval. Passing through a circle does not confirm
absence. Purchase trips may contribute valid coverage samples when they meet
the same dwell condition.

For full-market reconciliation, candidate checkpoints are actual retained
trader coordinates. A greedy set-cover pass chooses the point that covers the
most unverified traders within a radius slightly below the protocol's safe
2,000-unit range, then repeats until every retained coordinate is covered.
Using a conservative radius provides overlap and avoids boundary misses. The
route visits only the required checkpoints; the character does not return to
the market center between them.
