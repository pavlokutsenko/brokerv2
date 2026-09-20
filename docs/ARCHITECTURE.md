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

Runtime.Radar
  WorldLocator          GWorld discovery/validation
  ActorSnapshotReader   coherent actor-array read
  TraderMapper          actor -> domain trader
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
launch and claims only a newly observed `lu4.bin`. Manual attachment chooses an
unclaimed process. Process paths are hints only because the protected client
may deny `ExecutablePath` reads.

## Size and dependency rules

- Prefer one responsibility per source file.
- Split implementation files before they exceed roughly 250 lines of logic.
- Binary layouts and offsets live in versioned profile records, not scattered
  constants.
- Driver transport is read/write mechanics only; radar, broker and movement
  behavior never accumulate in one driver class.
- Every hook has an owner session and a `finally` rollback path.
- Research JSON support is a temporary adapter and cannot leak into domain
  models or future server contracts.

## Current milestone

The first UI milestone implements profiles, multi-process ownership, client
launch/attachment and the broker/radar dashboard. Current metrics are supplied
by a replaceable research JSON adapter while the native read-only radar runtime
is ported behind the same application boundary.
