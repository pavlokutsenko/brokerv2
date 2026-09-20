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
