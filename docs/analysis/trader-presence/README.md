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

## Central-zone rule

Each collector profile stores one user-marked market center with a fixed radius
of 500 world units. The market is known to be fully visible while the
character is inside this zone.

Centers are stored separately for each city in the profile. Changing the city
loads its saved center; marking or resetting affects only that city.

Collection has one explicit runtime start/stop toggle independent of the game
client. Launching the client leaves collection stopped. The catalog changes
only when collection was started and the character is inside the central zone.
Stopping collection freezes the catalog without closing the client or removing
the packet hook.

Presence has three states:

1. `visible`: present in the packet knownlist while collection is active;
2. `frozen`: retained exactly as last observed while the character is outside
   the central zone;
3. `gone`: still absent after the character returns to the central zone and the
   three-second packet-settle interval completes.

Entering the zone rebuilds current visibility from the live actor table. A
supported `CharInfo` refreshes a trader. Previously known traders that remain
absent after settling are marked inactive regardless of their individual
coordinates, because the center provides complete market visibility. Leaving
the zone cancels pending absence checks and freezes the catalog. Packets seen
during purchase trips do not add, remove or move catalog entries.
