# HerzBot radar mechanism

## Confirmed result

HerzBot does not depend on `GWorld` or the Unreal actor array for its normal
radar. It reconstructs world state from decrypted game packets. This explains
why ordinary LU4 executable updates do not require a new Unreal-global RVA.

The analyzed HerzBot build is:

- source image: `C:\D\herz_hack\offline_1.0.50\offline\herzbot.exe`;
- unpacked live reconstruction used for static analysis:
  `C:\Users\Pavel\Documents\ChatGPT\driver\diagnostics\herzbot-live-reconstructed.exe`;
- PE timestamp: `0x6A9CFEE9`;
- image size: `0x032CD000`.

## Evidence

The unpacked image contains and uses:

- `Lu4ReadHook` with an AOB scan and original-byte validation;
- a dynamically allocated `RECV ring` and persisted ring marker;
- a background `PacketReader` with receive-only and receive/send modes;
- packet handlers for `NpcInfo`, `CharInfo`, `DeleteObject`, `StatusUpdate`,
  `MoveToLocation`, and `StopMove`;
- an explicit startup failure stating that the bot cannot read packets when
  hook installation fails.

The current LU4 read-hook search pattern embedded in that build is:

```text
44 0F B6 C2 41 88 4A FF 44 3B CE 7C ?? 48 01 75 50
```

The wildcard branch displacement lets the signature survive relocation and
small compiler-layout changes. HerzBot scans the loaded client range, requires
a match, verifies the original bytes, allocates a datacave and receive ring,
then patches the matched function. The hook copies already decrypted packets
into the ring. The external process reads the ring and updates its own entity
table.

The kernel driver is transport: process reads/writes and remote memory
operations. Packet parsing, entity lifetime and radar state live in user mode.

## Consequences for this collector

The production radar should use the same architecture:

1. Scan executable client sections for a versioned AOB signature.
2. Require exactly one match and validate expected original bytes.
3. Allocate a bounded datacave and SPSC receive ring through `LU4Memory`.
4. Install a minimal hook with an owner session and exact rollback data.
5. Read coherent ring records through the driver.
6. Maintain entities from spawn/update/move/delete packets.
7. Map private-store entities to collector traders and emit lifecycle events.

The existing driver already exposes the primitives needed for this design:
bounded read/write plus allocate/protect/free in the selected process. Hook
layout, ring schema and packet decoders belong to user-mode modules and must
not be added to the driver.

## Update behavior

This removes Unreal offsets from the normal radar update path, but it is not
guaranteed to survive every client or server change. The runtime must fail
closed when the hook signature is missing or ambiguous, original bytes differ,
the ring is incoherent, or packet layouts stop passing validation. Such a
change requires a new signature or decoder profile; an ordinary RVA shift does
not.

`GWorld` discovery remains useful for diagnostics and independent validation,
not as the production source of trader presence.

## Live validation on the current client

The approach was validated after a full client relog on 2026-09-20. The AOB
again resolved to exactly one receive hook, and a bounded diagnostic ring
captured 6,458 decrypted packets without closing the client. Of those, 394
were `CharInfo` (`0x31`) and every one passed the recovered structural parser.

The current `CharInfo` prefix is:

```text
byte    0        opcode = 0x31
byte    1        variant flag
uint32  2        ObjectID
utf16z  6        character name
utf16z  variable title
byte    title_end + 31   kiosk type
int32   title_end + 40   X
int32   title_end + 44   Y
int32   title_end + 48   Z
```

The final packet state contained 227 supported private shops: 174 Sell
(`1`), 35 Buy (`3`) and 18 Package Sell (`8`). It also exposed Craft (`5`),
which the collector can discard. An independent actor-memory snapshot matched
the packet name and kiosk type for 222 of 223 comparable traders. The only
disagreement was a trader whose packet field changed between `2` and `1`
around the snapshot, so it is a timing/state-transition sample rather than a
layout mismatch.

The diagnostic ring had only three slots because the current driver allocation
request is capped at 64 KiB. It is sufficient to prove the hook and decoder,
but the production ring must be larger or drained in-process to avoid drops
during a dense relog burst.

## Why HerzBot shows the full market in real time

HerzBot installs the receive hook as part of its client-launch/attach sequence,
before the character enters the world. Its `PacketReader` then owns a durable
entity `knownlist`:

- `NpcInfo` and `CharInfo` create or replace entity records;
- `MoveToLocation` and `StopMove` update their live coordinates;
- `StatusUpdate` updates mutable state;
- `DeleteObject` removes an entity from the knownlist.

The radar renders this retained table. It does not expect the server to resend
every visible character on every refresh.

This was isolated with a second probe that copied only opcode `0x31`, used 63
slots inside the same 64-KiB allocation, and drained every millisecond. Attached
to an already populated world, it captured 370 `CharInfo` packets with exactly
zero ring drops, but still did not reconstruct the roughly 1,700 existing
traders. Therefore the reduced count in the first experiment was not only a
ring-capacity problem: a late packet hook has no initial world snapshot.

The collector needs two explicit startup paths:

1. **Managed launch:** start the client, attach the driver and install the hook
   before world entry, then build the entity table entirely from packets.
2. **Late attach/recovery:** take one coherent actor snapshot through the
   driver, seed the same entity table, and immediately continue with packet
   create/update/move/delete events.

Both paths converge on one user-mode entity store. Periodic full actor-array
polling is unnecessary after bootstrap; a bounded reconciliation scan can be
kept only as a health check.
