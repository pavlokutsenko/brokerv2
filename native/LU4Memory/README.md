# LU4Memory

This directory is the source-owned kernel runtime for the collector. The public IOCTL contract is in `include/lu4_protocol.h`. Keep the matching managed client contract in sync whenever the protocol version changes.

The implementation is split by responsibility and compiled as one translation unit through `driver/driver.c`. This preserves the behavior of the verified baseline while keeping each source module small:

- `process_memory.inc` — process base lookup and bounded reads/writes;
- `target_mailbox.inc` — target-command handoff;
- `active64.inc` — `active64.sys` lookup and bounded reads;
- `packet_transform.inc` and `packet_crypto.inc` — packet transform and guarded state update;
- `virtual_memory.inc` — allocation, protection, and release;
- `proxy_redirect.inc` — per-process IPv4/TCP relay and route ownership;
- `proxy_policy.inc` — inherited relay-only policy, UDP/raw/IPv6 denial and fail-closed owner exit;
- `proxy_guard_query.inc` — capability/route/blocked-attempt readback;
- `dispatch.inc` — IOCTL routing and driver lifecycle.

## Build

Run from an ordinary PowerShell terminal with Visual Studio 2022 Enterprise, Windows SDK `10.0.26100.0`, and WDK installed:

```powershell
cd C:\broker\native\LU4Memory
.\build.ps1
```

The reproducible output is written to `build\x64\Release\lu4_memory.sys`. Intermediate and local build output stays outside Git.

## Verified baseline

The 2026-09-27 artifact is verified with benign authenticated WFP routing,
IPv4/IPv6 TCP and UDP denial, child inheritance, relay/controller revocation,
suspended startup and concurrent memory reads. Its SHA-256 and scope are in
artifacts/verified/manifest.json. The old live-tested WFP baseline is superseded;
this binary still requires guarded LU4 live acceptance. A fresh source build
stays separate until runtime checks pass; building never replaces the service.
Promotion also updates the pinned loader hash.

Protocol remains version 3 with an additive QUERY_PROXY_GUARD IOCTL.
Capabilities 15 identify route readback, direct/protocol denial, inheritance
and fail-closed owner exit. Desktop requires all four before game creation.
See [launch protection](../../docs/LAUNCH_PROTECTION.md) for checks and limits.

Run `verify-verified-build.ps1` to check the committed binary against its manifest before loading it.
