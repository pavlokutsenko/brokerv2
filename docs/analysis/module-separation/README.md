# Launch and collection modules — 2026-09-24

Request: one WPF window, two independent modules. The previous launch path
installed the packet radar before login and waited for first-client coordinates
before starting the second CLI-selected profile. This coupled launching to
reader compatibility and readiness.

## Implemented boundary

| Project | Owns |
| --- | --- |
| `src/PriceCheck.Launching` | `LaunchModule`, game process ownership, existing identity/template code, native agent deployment, proxy lifetime and automatic login |
| `src/PriceCheck.Collection` | `CollectionModule`, radar and character reads, broker scheduling, price queue/workers and upload services |
| `src/PriceCheck.Contracts` | Shared configuration and data records, `ClientSession(PID, StartedAtUtc)` |
| `src/PriceCheck.Windows` | Shared driver transport/bootstrap and process identity queries |
| `src/PriceCheck.Collector` | WPF host, profile persistence and explicit composition of the two modules |

The two functional assemblies reference neither each other nor the WPF host.
Existing moved service/model namespaces are retained to avoid unrelated API
churn. Broker and price workflows were moved out of the window into separate
collection coordinator partials; their existing scheduling policy is retained.
Native launch/login DLLs, kernel driver, offsets and broker payload are unchanged.

One window has Launch (Profile / Templates) and Collection sections. Launch
does not install a radar or start a collection/upload worker. Collection attaches
explicitly to the selected profile's client or an unbound existing process.
Disconnect disables new collection work, drains its current worker and removes
the reader. It cannot release the launcher's proxy or terminate a game.
A failed reader attach leaves the launched client running. Launch failures and
explicit Stop client remain the launch module's responsibility.

Disconnect waits up to two minutes for an in-flight collection job. If that wait
expires, the reader remains attached with collection disabled; retry after the
job completes. It is not reported as successfully disconnected. Worker cleanup
is checked before detaching. The game is not killed to force a reader stop.
Closing the entire host still stops clients launched by that host and their
proxies, after reader cleanup. A discovered/restored external client is not
adopted as a launcher-owned process and is left open on host shutdown.

PID and start time are checked on attach, refresh and worker execution. An
in-flight attach reserves its PID; another profile cannot attach to it. A stale
remembered PID without a start time is not automatically restored or read.
Receive-hook cleanup checks its original process identity before writing or
freeing memory. A price target must match a currently visible ObjectID and
normalized trader name; a persisted old session token is insufficient.

Profile/template files and DPAPI credentials remain under LocalAppData with
their existing format, durable IDs, fixed HWIDs and world seeds. The optional
`LastProcessStartUtc` field is additive. Duplicate display names are no longer
collapsed by market/city during loading. Profile persistence is host-owned;
modules do not run competing configuration writers.

## Validation and limits

- Root `build.ps1 -OutputDirectory workspace\module-separation-publish`: passed,
  self-contained Windows x64, existing staged BrokerWorker reused.
- `tests/ModuleIsolation.Smoke`: fake solo/pair launch and detach, reader failure,
  duplicate PID, PID reuse, launch failure, pending attach reservation, draining
  a pending worker and refusing restart during detach; assembly boundaries pass.
  These are lifecycle tests, **not** live solo/paired game controls.
- `tests/ClientLaunch.Smoke --world-identity-checks`: passed after source move;
  existing identity calculation remains unchanged.
- `tests/CollectorUi.Smoke`: two module tabs, nested launch templates, proxy
  binding/save and seed preservation; offscreen renders at widths 1380 and 1120.
  Final test verifies production settings stay byte-for-byte unchanged.
- Loaded release and current games were not replaced or restarted. New managed
  launch/reader integration still needs explicit solo and paired live controls
  after the current 24-hour observation, including attach/detach in the world.

The reader receives packets only after attachment. Late attachment cannot
reconstruct previously received scene packets; attach before manual world entry
for full initial coverage or wait for new scene updates. Module separation does
not make either module compatible with arbitrary game updates: auto-login and
identity hooks have their own version assumptions; character/price readers have
separate offsets and layouts. No hot replacement of a loaded assembly is added.

## UI-test side effect and repair

The first offscreen host render pumped WPF's queued `Application.OnStartup`,
although the test never called `Run`. This invoked production initialization and
cleared old saved PID bindings that lacked the newly required start time.
It also invoked the normal driver readiness check; no driver reload is observed
and the original game processes remained alive. This was an unintended settings
write, not a live integration test.

The monitor recorded event 6 at **11:37:58 UTC** (`profile_binding_changed` for
both clients) and event 7 at **11:43:48 UTC** (healthy restored). Its 35 intervening
samples skipped positions. All 35 retained matching original processes,
established TCP and fresh exchange (maximum rx age 4 s, tx age 30 s), with no
EOF. This interval has partial observation coverage, not a game disconnect.

Original bindings were repaired only after matching profile IDs, PID and exact
process start times from the private monitor cursor. Pre-repair settings and a
repair record are private under the run's `ui-smoke-side-effect` folder. No
account, template, proxy or identity value was intentionally changed. Templates
and profiles were re-saved by the unintended initialization; original file bytes
before it were not captured, so a byte-for-byte rollback cannot be claimed.

The test now creates `App(enableRuntime: false)` and
`MainWindow(initializeRuntime: false)`, suppressing both startup paths. A rerun
checked production profile and template bytes unchanged. The original monitor
then continued and was stopped at the user's request at 12:13:58 UTC. Its
[final observed success and limits](../two-client-isolation/observations/2026-09-24-long-monitor.md)
apply to the running pre-refactor release; this modular build still needs live
validation. The affected samples remain in the history and are not relabeled.

## Portable output

Final archive: `release/packages/PriceCheckCollector-win-x64-20260924-144951.zip`,
84,887,335 bytes, 567 manifest-listed files plus the manifest.
SHA-256: `B68D12EF7D41981EF518A548E0EA88142952C15B3353B685B4096183BB3ADEBB`.
An independent extraction passed `Verify-Package.ps1` under Windows PowerShell
5.1. The package includes all four new managed library dependencies and updated
instructions, without user profiles, accounts, proxy addresses or research data.
All four native/runtime payload hashes match the still-loaded release:
ClientAgent, ClientLogin, LU4Memory and BrokerWorker. The old 13:22 archive remains
the pre-refactor build; the new archive has not been exercised against live LU4.

Existing large service files (ClientProcessService, ProxyTcpBroker and
LocalPriceQueue) were retained around their existing boundaries; this change
does not perform unrelated scheduling or proxy rewrites. New module lifecycle,
worker runner and UI files stay below the relevant size limits.
