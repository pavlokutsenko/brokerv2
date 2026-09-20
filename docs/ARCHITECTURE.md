# Collector architecture

## Repository boundary

This repository contains only the production collector application. Live
reverse-engineering scripts, packet probes, dumps and experiment artifacts stay
in the separate research directories and are never copied here.

## Modules

```text
UI
  Windows, controls and presentation state only

Application
  profile/session coordinator
  role state machines
  broker scheduling
  price-verification job execution

Domain
  collector profiles
  market/trader/shop generations
  broker epochs and stock deltas
  verification jobs and results

Infrastructure.Windows
  client launch and PID ownership
  LocalAppData persistence
  server transport

Runtime.Driver
  Lu4Device             IOCTL transport only
  ProcessMemory         bounded typed reads
  RemoteMemory          bounded allocate/protect/free

Runtime.Radar
  HookSignatureScanner  unique executable-section AOB resolution
  ReceiveHookSession    datacave/ring install and exact rollback
  PacketRingReader      coherent plaintext packet dequeue
  WorldPacketDecoder    packet -> entity updates
  TraderMapper          network entity -> domain trader
  RadarLifecycle        generations and transitions

Runtime.Broker
  BrokerHook            installation/rollback only
  BrokerProtocol        request and response structures
  BrokerEpochCollector  complete epoch orchestration
  StockDeltaBuilder     safe quantity changes
```

The runtime pieces communicate through application contracts. UI code never
calls an IOCTL, parses game memory, installs a hook or builds a packet.

## Native driver ownership

`native/LU4Memory` is the only source of truth for the collector driver. It
contains the versioned IOCTL header, responsibility-based kernel source
modules, the WDK build project and the last runtime-verified binary with its
hash manifest. Local compiler output remains ignored. A newly compiled driver
does not replace the verified artifact until its runtime checks have passed.

## Multi-instance ownership

One persisted profile represents one market client. A live PID can be claimed
by exactly one profile. Each profile owns its cancellation scope, radar loop,
broker schedule, server identity and event stream. Starting or stopping one
profile cannot replace global static state used by another profile.

The process launcher serializes client startup, records the PID set before
launch and claims only a newly observed `lu4.bin`. Manual attachment is not a
production path: the collector installs the packet hook before the character
enters the world, and failed hook setup terminates that owned client. Process
paths are hints only because the protected client may deny `ExecutablePath`
reads.

## Local configuration

All persistent collector settings are stored in `profiles.json` beside
`PriceCheck.Collector.exe`. On the first run after this change, the application
copies the previous `%LOCALAPPDATA%\PriceCheck\CollectorNext\profiles.json` when
the new file does not yet exist.

Central-zone coordinates belong to a profile and city. Switching between
`Giran` and `Gludio` selects that city's saved center automatically; marking or
resetting a center changes only the currently selected city. The market name,
city, role, client path, broker interval and city centers are persisted. Live
PID ownership and collection start/stop state are session-only.

## Size and dependency rules

- Prefer one responsibility per source file.
- Split implementation files before they exceed roughly 250 lines of logic.
- Binary layouts and offsets live in versioned profile records, not scattered
  constants.
- Driver transport is read/write mechanics only; radar, broker and movement
  behavior never accumulate in one driver class.
- Production radar is reconstructed from the decrypted receive-packet stream.
  `GWorld`/actor-array reads remain diagnostic fallback only and are not a
  normal client-update dependency.
- Every hook has an owner session and a `finally` rollback path.
- Research JSON support is a temporary adapter and cannot leak into domain
  models or future server contracts.

## Current milestone

The collector now owns client launch, signature resolution, receive-hook
installation, packet-ring draining and the in-memory entity table. Archived
research JSON is not displayed in production. Broker cards stay empty until a
live broker epoch owned by the same profile is implemented.

The runtime entity table and market trader catalog have different lifetimes.
`DeleteObject` removes an actor from the current knownlist but only marks a
catalogued trader as out of range. The trader's normalized nickname, last shop
type and coordinates remain available while a broker-role character leaves the
market and later returns. A visible non-shop `CharInfo` marks that trader as no
longer trading. Each profile has a user-marked central zone with a fixed
500-unit radius. The catalog accepts packet changes only while the character
has explicitly started collection and is inside that zone. Client launch and
collection start/stop are separate controls. Outside the zone or while stopped,
the last market state is frozen. On return,
the current actor knownlist is rebuilt and previously known traders that remain
absent after a three-second packet-settle interval are confirmed gone. Broker
epochs and the future SQLite writer will provide durable cross-session state.
