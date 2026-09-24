# Same-world diagnostics

The normal implementation is in `native/ClientLaunch/world_identity.cpp`.
For HWID-enabled launches, `ClientLaunchConfiguration` passes a distinct
16-byte world identity. Existing fixed templates retain their profile-ID value
until regenerated. New identities contain a saved `WorldIdentitySeed`; the
launch value is the first 16 SHA-256 bytes of profile-GUID bytes plus seed.
Regeneration rotates that value, while two profiles sharing a template still
differ. The validated packet path is the Gamma 37-to-69-byte handshake.

The agent checks the loaded Active Anticheat version, builder code signature,
packet location/length and encrypted-envelope integrity before rewriting a
copy for `send`. It only reads the already running `LU4Memory` interface;
it does not write driver globals, alter another process, or load a test driver.
An unsupported layout fails that send and leaves a diagnostic status. The
version checks must be revalidated after a client/anticheat update.

Non-secret status: `%LOCALAPPDATA%/PriceCheckCollector/logs/world-identity-PID.txt`.
Research captures and credentials must remain outside the repository.

Read-only verification (PowerShell, Python with no additional dependencies):

```powershell
python .\tools\TwoClientIsolation\inspect_live_worlds.py --profile Gamma --profile Black
```

This validates distinct persisted profile PIDs, the current client PE version,
and each player's coordinate chain. Combine it with live process/connection
checks and completed world entry; configured world names alone are not proof
of successful entry. It installs no hooks and does not load any driver.

## Long observation of an already running pair

Run with PowerShell 7 while both clients are connected and the existing
metadata tracing is enabled:

```powershell
pwsh -NoProfile -File .\tools\TwoClientIsolation\monitor-worlds.ps1 -Hours 24 -IntervalSeconds 10
```

Use `-PythonPath` if `python.exe` is not on PATH, and `-Once` for a single
read-only live smoke check. The monitor does not launch/stop games, load a
driver, change settings, install hooks or alter packets. It binds each profile
to its initial PID and process start time; changed bindings or reused PIDs
are observations, never silently followed. `LU4Memory` must already be running
for the existing coordinate observer, whose child process has an 8-second timeout.

Output is restricted to `%LOCALAPPDATA%\PriceCheckCollector\research\long-monitor`.
`current.json` points to the active run directory. The run contains atomic
`status.json`, append-only `samples.jsonl` and `events.jsonl`, and bounded
metadata tails in `event-NNNN`. Each sample combines live TCP, coordinate
validity, recent world traffic, message-length histograms and available IOCTL
failures. The first sample's deltas include the log history and are explicitly
marked `baseline`; subsequent reads are incremental. TCP log lengths describe
proxy read chunks, not guaranteed protocol packet boundaries. Native IOCTL
tracing stops at 4096 events; `cap_reached` records this limitation. TCP
metadata and process observation continue after that cap. No packet bodies,
credentials or proxy addresses are copied to this repository.

Create an empty `stop.request` inside the run directory to stop gracefully;
this leaves both clients running. A local mutex rejects a second long monitor.
Sleep/slow sampling gaps over 45 seconds (or three configured intervals) are
recorded, not counted as demonstrated uninterrupted connectivity. `healthy`
means all observed checks passed at that sample, not proof of the server's
policy or absence of delayed checks. Missing traffic over 90 seconds is
classified as `traffic_quiet`, not attributed to anticheat.

Metadata-parser smoke, using only synthetic temporary files:

```powershell
pwsh -NoProfile -File .\tests\TwoClientIsolation.Monitor.Smoke.ps1
```

Offline reproduction of the machine-code builder (requires `pefile` and
`unicorn`, already present on the research machine):

```powershell
python .\tools\TwoClientIsolation\replay_world_builder.py `
  --driver "$env:LOCALAPPDATA\Temp\ActiveAnticheat\1224067\active64.sys" `
  --capture "$env:LOCALAPPDATA\PriceCheckCollector\research\builder-source\paired-20260924-110355"
```

The tool rejects captures inside this repository, checks the driver SHA-256
against capture metadata, executes the builder in emulated memory, and prints
only hashes, counts, relative offsets and exact-match results. The two compiler
support stubs and reconstructed scratch arguments are documented in the
[observation](../../docs/analysis/two-client-isolation/observations/2026-09-24-builder-source-continuation.md).

Use root `build.ps1 -SkipBroker` for publication after clients release their
DLL handles; use `-OutputDirectory workspace/build-verify` while they run.
The old release binaries from before this change are backed up outside the
repository under the same LocalAppData research directory. Never restore DLLs
while a client holds them open.
