# Local player position

## Problem

The receive packet radar updates other actors, but LU4 does not echo the local
character's movement through the same incoming `MoveToLocation` stream. The
local `CharInfo` position therefore remains the login position and the radar
center appears frozen while the character runs.

## Confirmed current profile

- PE timestamp: `0x956E0D97`
- image size: `0x0DCEB000`
- `GWorld` RVA: `0x081F6A80`
- chain: `GWorld -> +0x1D8 GameInstance -> +0x38 LocalPlayers -> [0] ->
  +0x30 Controller -> +0x2D0 Pawn -> +0x1A0 Capsule`
- position: three doubles at `Capsule + 0x1F0`

The collector now resolves this chain once and reads only the cached capsule
position for each UI snapshot. A stale or invalid capsule clears the cache and
causes one guarded re-resolution. The profile is accepted only when both PE
timestamp and image size match; otherwise packet radar continues with its
previous center fallback.

The pawn can be replaced after initial world entry while the old capsule memory
remains readable with frozen coordinates. Each snapshot therefore re-reads
`Controller + 0x2D0 -> Pawn + 0x1A0 -> Capsule` and updates the cached capsule
before reading position. `GWorld` is resolved again only if the cached
controller chain becomes invalid.

## Scope

Trader presence and movement still come entirely from the packet knownlist.
This path supplies only the exact local radar center. It does not scan the actor
array and does not poll `GWorld` continuously.

## Area-transition failure found during validation

The first long run ended because the 126-slot receive ring overflowed while the
character entered a new area. The consumer previously issued one driver read
and one read-index write per packet, so a spawn burst could outrun it. It now
copies the complete 64-KiB ring in one driver call, consumes every packet from
that stable snapshot, and publishes the final read index once. A future radar
failure restores the hook but leaves the owned game client running.
