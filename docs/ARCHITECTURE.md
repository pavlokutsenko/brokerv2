# Collector architecture

## Repository boundary

This repository contains the production collector and the minimal source set
needed to reproduce its broker worker. Runtime execution never reads scripts,
state or binaries from a separate research directory.

## Modules

The implemented managed boundary now consists of two independent functional
projects, `PriceCheck.Launching` and `PriceCheck.Collection`. The WPF
`PriceCheck.Launcher` host composes launching only; `PriceCheck.Collector`
composes both modules. `PriceCheck.Launching.UI` owns the common launch/template
views and desktop styles, referencing launching only. They share pure records in
`PriceCheck.Contracts` and Windows transport in `PriceCheck.Windows`, with no
reference between launch and collection. Launch owns game/proxy lifetime;
collection owns attach/detach, radar, broker and price workers. Closing a reader
does not close the game. Profile persistence is implemented in launching;
each host owns its settings directory and is its sole writer. Legacy public
namespaces and the pure `CollectorProfile` JSON contract remain compatible.
The launcher does not reference collection or the collector host.
See [desktop packages](DESKTOP_PACKAGES.md) for the two portable entry points.
Each portable root contains one desktop EXE and one runtime directory.
The SDK CreateAppHost task binds the EXE directly to runtime/PriceCheck.<Product>.dll,
preserving GUI/UAC resources and process identity; no wrapper process is added.
AppContext.BaseDirectory resolves to runtime, so native/driver/worker lookups
remain unchanged. Runtime dependencies, package verification and recovery
metadata live there; profile settings remain in LocalAppData.
The SDK supports relative app binary paths through
[HostWriter.CreateAppHost](https://github.com/dotnet/runtime/blob/v8.0.24/src/installer/managed/Microsoft.NET.HostModel/AppHost/HostWriter.cs).
See [module separation](analysis/module-separation/README.md) for API/lifecycle,
validation, migration and live-test limits. The following diagram describes
responsibilities; it is not a list of additional projects.

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

Each collector profile owns a nullable launch-template ID. Reusable templates
hold a fixed or rotating generated identity plus authenticated HTTP
proxy settings. Both HWID and proxy are mandatory for game launch; an empty
template ID is a configuration state that cannot launch a client. The
template values are snapshotted into the child process environment before
`CreateProcessW(CREATE_SUSPENDED)`. WFP is bound before resume and inherited by
descendants before their first thread runs. The launcher waits for the final
client's GUI window and uses a thread-local Windows message hook to load its
own agent DLL into that process. The agent pins itself after installing the
identity/proxy hooks, reports readiness, and the launcher then removes the
temporary message hook. It does not stage a DLL beside the game EXE. The
agent reports readiness for the final `lu4.bin` PID before radar setup. It
intercepts identity APIs and gates socket sends;
the packet radar remains a separate driver-backed hook in the game image, and
the broker worker keeps its own temporary hook lifetime. A LocalAppData
deployment record identifies an older staged `version.dll` for hash-checked
cleanup; unrelated files are left alone. The message-hook loader is independent
of HardShift's unconfirmed driver injection primitive.

ClientLaunchGuard owns a random per-launch shared memory lease identified by
PID and process birth time. Native checks read the installed hooks back,
validate the supported layout before login and the envelope at actual world
send, and report each successful HWID rewrite. Driver process metadata supplies
PID lifetime and creation time without requiring an external user-mode handle.
The broker independently counts CONNECT generations and bidirectional traffic;
an old world's confirmation cannot authorize a new world. Native login/send
checks and continuous monitors stop the client on failure. WFP permits only
the exact IPv4/TCP relay endpoint and denies other outbound protocols/routes,
including after controller exit. UI reads ClientProtectionStatus through the
launch module. See [protection checks and limits](LAUNCH_PROTECTION.md).

The Templates tab scans local firmware, registry, network adapters, volumes,
physical disks, monitor EDID and bounded PnP registry entries in memory. It
separates agent-hooked fields from discovered-only fields. The user-mode agent
currently patches 20 template values through SMBIOS, registry, volume, adapter,
SendARP and storage-query APIs. The ARP MAC replacement is limited to the
gateway IPv4 address detected on the machine at launch. WMI-provider and
kernel reads remain outside its scope.

## Local configuration

Collector profiles and launch templates are stored in
`%LOCALAPPDATA%\PriceCheckCollector\profiles.json` and
`%LOCALAPPDATA%\PriceCheckCollector\launch-templates.json`. On first run after this
change, the application copies the portable `profiles.json` beside
`PriceCheck.Collector.exe`, or the older
`%LOCALAPPDATA%\PriceCheck\CollectorNext\profiles.json` when available.
Proxy passwords are encrypted for the current Windows user with DPAPI.
Launcher uses the same serializer and encryption under
`%LOCALAPPDATA%\PriceCheckLauncher`, with no automatic import or modification of
the collector store. A named per-product desktop lease prevents concurrent UI
writers, including copies launched from different extracted directories. The
collector upload worker does not claim that UI lease. Copy both configuration
files deliberately to migrate; durable profile/template IDs and world seeds
are preserved, and machine-bound passwords must be entered on the destination.

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
installation, packet-ring draining, the in-memory entity table and live broker
epochs. Archived research JSON is not displayed in production.

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
# Local market server integration

For current LU4 same-world entry, the launch boundary also passes a 16-byte
world identity when hardware identity is enabled. New generated identities
persist a `WorldIdentitySeed`; hashing it with the durable profile ID keeps
profiles distinct and makes Regenerate rotate the world value too. Existing
fixed templates without a seed retain their earlier profile-ID value until
regeneration. `world_identity.cpp` owns the protocol-specific
operation. It verifies the supported Active Anticheat build and a shared
encrypted envelope through read-only LU4Memory requests, then changes a copy
of the initial 69-byte send. It never modifies driver globals or another
profile's process. The UI/domain have no packet parsing or driver offsets.
Per-PID status files contain only success/error names, not packet contents.
This isolates the validated world-entry signal, not all Windows or driver
state. Evidence is in `docs/analysis/two-client-isolation/HANDOFF.md`.

The collector sends complete radar presence snapshots to
`POST /ingest/market-snapshot` on the profile's `ServerUrl` (default
`http://127.0.0.1:3021`). Uploads only run while collection is enabled and the
character is inside the configured center zone. Stable identity is the
normalized trader name; the current object ID is observation metadata.

The sender uploads immediately when the visible trader set changes and sends a
heartbeat after ten seconds without changes. Radar batches never claim that a
shop inventory is complete. Broker inventory is a separate source and can mark
one trader's item list complete without coupling radar state to shop contents.

For `BrokerRadar` profiles, enabling collection also starts a broker inventory
cycle immediately. The bundled `BrokerWorker` discovers the current client's
addresses, installs its PID-scoped temporary hooks, queries store types 1, 3,
and 8, rolls the hooks back in `finally`, and returns one complete inventory.
The collector uploads that inventory and repeats at `BrokerIntervalMinutes`.
Broker work runs asynchronously while the receive radar continues. A single
global broker gate serializes the temporary ProcessEvent capture when several
profiles exist.
