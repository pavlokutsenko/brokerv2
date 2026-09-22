# LU4Memory

This directory is the source-owned kernel runtime for the collector. The public IOCTL contract is in `include/lu4_protocol.h`. Keep the matching managed client contract in sync whenever the protocol version changes.

The implementation is split by responsibility and compiled as one translation unit through `driver/driver.c`. This preserves the behavior of the verified baseline while keeping each source module small:

- `process_memory.inc` — process base lookup and bounded reads/writes;
- `target_mailbox.inc` — target-command handoff;
- `active64.inc` — `active64.sys` lookup and bounded reads;
- `packet_transform.inc` and `packet_crypto.inc` — packet transform and guarded state update;
- `virtual_memory.inc` — allocation, protection, and release;
- `dispatch.inc` — IOCTL routing and driver lifecycle.

## Build

Run from an ordinary PowerShell terminal with Visual Studio 2022 Enterprise, Windows SDK `10.0.26100.0`, and WDK installed:

```powershell
cd C:\broker\native\LU4Memory
.\build.ps1
```

The reproducible output is written to `build\x64\Release\lu4_memory.sys`. Intermediate and local build output stays outside Git.

## Verified baseline

`artifacts\verified\lu4_memory.sys` is the exact `candidate5` binary already exercised by the collector workflow. Its expected SHA-256 is recorded in `artifacts\verified\manifest.json`. A fresh source build is kept separate until it has passed the same runtime checks; building does not replace the registered or running service.

Run `verify-verified-build.ps1` to check the committed binary against its manifest before loading it.
