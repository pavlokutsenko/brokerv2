# PriceCheck collector — continuation checkpoint

Updated: 2026-09-24 (Europe/Kiev)

Managed refactor: one WPF window with independent `PriceCheck.Launching` and
`PriceCheck.Collection` assemblies. Launch no longer installs the radar;
Collection has explicit connect/disconnect and drains jobs before cleanup.
Build and fake lifecycle/UI tests passed; loaded release was not replaced.
Use [module separation](analysis/module-separation/README.md) before continuing.
It records a repaired UI-test settings side effect and the resulting 35-sample
position-observation gap; TCP/traffic continued and full checks recovered.

Successful same-Gamma observation completed at the user's request:
2026-09-24 09:35:28–12:13:48 UTC, 951 checks over 2 h 38 min 20 s,
no new world EOF. 914 fully healthy samples; 35 missing coordinate checks
and 2 short metadata backlogs remain documented. No missing sample intervals.
The monitor exited normally at 12:13:58 UTC, automation `gamma` is PAUSED;
do not restart it automatically. Both original clients and established TCP
remained present after stopping. Private `final-summary.json` under LocalAppData
`PriceCheckCollector/research/long-monitor/20260924-123527` has the totals.
This is success for the observed working release/configuration, not a completed
24-hour test or validation of the new modular build/another PC. See HANDOFF.

Resumed with new accounts and regenerated fixed HWIDs. Old proxy settings
were restored at the user's request. Collector Regenerate now also rotates
the world-request identity using a saved seed, preserving durable profile IDs.
Matched controls passed: 420 seconds solo and 602 seconds paired (116 valid
samples) on restored original proxies. Both clients were left open. Current
evidence and periodic-message analysis are in the same-world HANDOFF.

Earlier same-world entry checkpoint (superseded by the long control above):
Gamma and Black enter Gamma simultaneously using
the normal published Collector, but after a successful 120-second control
both connections were absent on a later check without user intervention.
Sustained success is not yet established. A controlled change of 16 stable bytes in the
added world-request field passed solo and paired controls, and is implemented
in the native agent using the saved profile ID. No test driver or kernel-state
mutation is needed. Start with `docs/analysis/two-client-isolation/HANDOFF.md`
for current release hashes, live verification, limitations and rollback.
Older launch/PID and unverified same-world statements below are historical.

Launch-template UI review: proxy fields now enable immediately when checked, the proxy form appears before expandable HWID/scan details, and user-facing WPF text is English. The light ComboBox/dark TextBox contrast was corrected; an offscreen WPF smoke verified input binding, Save, and layout. See `docs/analysis/template-ui/README.md`.

## Current launch status

The separate Templates UI, per-process 20-field HWID agent, GUI-hook loader, and programmatic account/Gamma/first-character entry are in the working tree. A new PID-scoped WFP connect redirect and `ProxyTcpBroker` now handle optional authenticated HTTP CONNECT without replacing the game's receive path. A benign socket test and protected LU4 local-proxy tests passed through world entry; the supplied external proxy passed preflight and sustained world traffic twice. The portable Collector was rebuilt and restarted as PID 12800 with the new driver. Read `docs/analysis/wfp-proxy/README.md` and `RESUME.md` for evidence and the transient Active Anticheat Test Mode observation immediately after driver loading. Early root/child DLL injection remains experimental (`PRICECHECK_EARLY_LAUNCH=1`) and fails game-window validation even with no spoof/proxy hooks.

The HardShift trace established `lu4.bin -> HardShift loopback listener -> proxy -> world server` and a WFP callout/filter at `FWPM_LAYER_ALE_CONNECT_REDIRECT_V4`. This guided our independent WFP implementation. Exact HardShift callback code remains unknown; see `docs/analysis/hardshift-proxy/README.md`.

## Earlier client launch identity and proxy checkpoint

The collector now supports a separate launch-template tab with 20 generated
HWID values, local hardware scanning, per-profile template binding and an
optional authenticated HTTP CONNECT proxy configuration. Static HardShift and TraceX research and the new
independent native launch agent are documented in
`docs/analysis/hardshift-launch/README.md`. Local smoke and two live LU4
launches reached “Радар готов”; the proxy run observed authenticated CONNECT
to the game's `:11000` endpoint. The temporary test proxy and credentials were
removed. The Gamma profile has HWID generation enabled and proxy disabled.
The latest 20-field live launch also reached “Радар готов” with final
`lu4.bin` PID 10076 and agent `ready`; it was stopped. The collector is running;
no test game client remains. The temporary `version.dll` was removed from the
game bin directory with the hash-checked rollback script.

A later live HardShift trace confirmed that its `HsAgent64.dll` is mapped into
the final client without an adjacent `version.dll`; its driver receives
per-PID profile requests. Static disassembly found an injection IOCTL with PID
and two DLL paths, but not its kernel implementation. Our first driver-loader
candidate failed on protected LU4 and was rolled back. The verified LU4Memory
driver is running from the release path, and the candidate IOCTL is absent from
shipping source. The launcher now uses a Windows GUI thread hook to load its
own agent DLL into the final client, without a game-adjacent `version.dll`.
The agent is pinned after its HWID/proxy hooks are installed. A live LU4
template launch passed agent readiness, survived 20 seconds after removing the
loader hook, and left no `version.dll` beside the game. A second live launch
installed the driver-backed receive hook and produced a radar snapshot. The
native identity/authenticated HTTP CONNECT smoke passed with the rebuilt agent.
`docs/analysis/driver-agent-load/PROGRESS.md` records the result.

## Start here

- Repository: `C:\broker`
- Read `AGENTS.md`, this file, then `docs/PRICE_VERIFIER.md`.
- Branch: `main`.
- Portable application: `C:\broker\release\PriceCheckCollector\PriceCheck.Collector.exe`.
- Normal development cycle: `cd C:\broker; .\dev.ps1 run`.
- Full packaging check: `cd C:\broker; .\dev.ps1 full`.
- Scheduled task `PriceCheck Collector Standalone` points to the portable EXE in `C:\broker` and is currently running.
- The market API, website and PostgreSQL runtime are separate in
  `C:\broker-server`. Scheduled tasks `PriceCheck Market API` and
  `PriceCheck Market Web` run from that path.

## Preserved local state

- `release\PriceCheckCollector\profiles.json` contains the Gamma / Giran profile, game launch path and saved centre `(82413.619, 148116.979)`.
- `release\PriceCheckCollector\data` was preserved, including the local price queue and server outbox.
- The only preserved manual capture is `release\PriceCheckCollector\manual-captures\manual-passby-20260921-005751.{json,jsonl}`.
- At this checkpoint the Collector is running. The separate `--upload-worker`
  child is expected. No game client was running during the final driver check;
  always discover current PIDs again.

## Driver startup status

Automatic driver loading from the portable Collector is working. A startup
failure after the repository move was traced to `load-driver.ps1` being UTF-8
without BOM: Windows PowerShell 5.1 decoded its Cyrillic messages as ANSI and
failed before the script could create a log. The source loader now has a BOM,
the bootstrap uses an argument list, and diagnostics use the no-space path
`%LOCALAPPDATA%\PriceCheckCollector\logs\driver-bootstrap.log`.

Final live check on 2026-09-23: service `LU4Memory` was `RUNNING` from the
bundled `C:\broker` driver, and an independent IOCTL read returned `MZ` from
the Collector process image base.

The development flow is documented in `docs\DEVELOPMENT.md`. `dev.ps1 run`
stops the previous UI, performs an incremental build, validates the bundled
loader with Windows PowerShell 5.1, launches through the highest-level
scheduled task and waits for both the main window and the running driver.
`dev.ps1 restart` does the same readiness check without rebuilding. A repeated
incremental run was validated in 6.2 seconds; restart completed in 4.5 seconds.

## Current working path

The reliable price-reading workflow is manual movement:

1. Start Collector and launch the game client through it once. This loads LU4Memory and creates a PID-scoped embedded runtime.
2. Enter the character and leave the game client running.
3. Run:

```powershell
cd C:\broker
.\manual-passby-shops.ps1
```

If several clients exist, pass `-ClientPid <pid>`. Stop with `Ctrl+C` so the temporary incoming-opcode hook is rolled back normally.

The reader refreshes the live actor array, reads each previously unseen trader entering the 95-unit radius and retries failures after 30 seconds. It uses an eight-request sliding window over the ProcessEvent capture ring. Hot batches do not cancel the target. Incoming opcode `0x72` is redirected to `RET` only while the reader runs, preventing shop responses from replacing the user's current route; the original handler is restored in `finally`.

Latest live validation: 450 unique traders in 141.602 seconds, 90 retryable failure events, overall 3.178 shops/s including movement and idle time. Successful batches commonly reached 14–17 shops/s. Artifact: `manual-passby-20260921-005751.json`.

## Decisions and limitations

- User rejected the current automatic route because movement and price requests were not smooth enough. Leave it alone unless the user explicitly returns to automatic routing.
- The manual reader does not send movement commands. The user controls the character.
- Exact shop rows include side, item ID, quantity, price and enchant. Broker data is used for membership/quantity refresh and must not overwrite exact price/enchant variants.
- Never copy `latest_*.json` diagnostic caches between PID sessions. `run_manual_passby` refreshes the live actor snapshot before hook preparation to prevent stale-PID failures.
- A forced worker termination can bypass Python cleanup. Prefer `Ctrl+C`; after an abnormal kill, verify `incoming_opcode_suppress_state.json` and restart the client if rollback state is uncertain.
- Runtime radar is based on the receive hook. Actor-array scans are the diagnostic/manual fallback used by the pass-by reader.

## Repository state and cleanup

The repository was moved from `C:\Users\Pavel\Documents\ChatGPT\pricecheck-collector` to `C:\broker`. Documentation and the scheduled task were updated. A clean build succeeded from the new path.

Reproducible debris was removed: old PID runtime sessions, broker/price/movement snapshots, verification publishes, screenshots, `bin/obj`, PyInstaller `stage/dist`, generated BrokerRuntime copies, Python caches and stale `latest_*.json` files. The portable release, profiles, data, verified driver artifacts, source and final manual capture remain.

## Relevant commits

- `9237eaa` — update collector paths for `C:\broker`.
- `a7b29e2` — keep manual shop reads from interrupting movement.
- `ee44c2b` — add fast manual pass-by shop reader.

## First checks in the next session

2026-09-23 proxy login checkpoint: the Black profile's saved proxy username had an extra literal backslash before `_`, causing CONNECT 407 and an LU4 disconnect. Both saved template usernames were corrected without changing the DPAPI-protected passwords. Proxy-enabled launches now verify authenticated CONNECT before starting LU4. The Black saved-profile live smoke completed account, Gamma and character selection with CONNECT 200 for login and world; its screen showed the world scene. Details: `docs/analysis/wfp-proxy/README.md`.

2026-09-23 two-client checkpoint: Gamma PID 16828 remained in the world, while Black failed at server selection via its own proxy and also without a proxy. The world connection was closed remotely after the client's 69-byte follow-up request. The two templates' 20 stored identity values differ and their proxy credentials produced different external IPs. HardShift also fails to keep these accounts together on this PC, according to the user. A second-client `LOCALAPPDATA`/`APPDATA` override did not relocate LU4's Windows Known Folder or solve entry. Do not claim the cause is a specific HWID check or add a speculative hook. The user subsequently confirmed that the two accounts can coexist from two physical PCs on one network. Details: `docs/analysis/two-client-isolation/README.md`.

Update: user confirmed these same two accounts enter simultaneously from two physical PCs. The restriction is tied to a same-PC observation or local shared state, but its exact input is still unknown. Clarify whether the unsuccessful HardShift test used one shared Random template or distinct identities before using it as a control.

Further clarification: the two physical PCs were on one network, so a single public IP was accepted. HardShift's saved configuration has one client using `Random` and the other using no template; its two-window failure was not a test of two distinct spoofed identities.

Early-interception test: an opt-in root loader resumed the second `lu4.bin` before injecting at 0/500/1000 ms. In all three runs the agent intercepted the two early `GetSystemFirmwareTable('RSMB')` calls, then the protected client exited during Active Anticheat checks before a stable game window. The experimental code was removed and the native binaries rebuilt. This does not prove SMBIOS causes the concurrency rejection; a supported earlier loading method or server-side reason is still needed. The live first client was not stopped.

An opt-in `DeviceIoControl` trace on the second client observed `0x222158`, `0x22215C` and `0x222160` at the world TCP exchange. Static Ghidra analysis found these exact values in one `active64.sys` dispatch function. This links the anticheat driver to the exchange but does not reveal the requested identity values or rejection reason. The driver was only inspected, never changed.

A follow-up opt-in trace of the second client recorded the three calls' sizes and status: `0x222158` input 164/output 4 bytes, `0x22215C` input 40/output 0, `0x222160` input 59/output 0. Every call succeeded with Win32 last-error zero; world entry still failed. The local driver does not reject these requests at the API boundary. No buffer contents were logged. Details: `docs/analysis/two-client-isolation/README.md`.

Caller attribution: all three IOCTLs are issued by the runtime-loaded `clmods64.dll` at RVAs `B5CE/B63F/B68F`. Its on-disk `.text` has no raw bytes, so ordinary static analysis misses the calls. A read-only, opt-in snapshot of the second test process's live `.text` confirmed wrappers for 164/40/59-byte inputs and a four-byte return value for the first call. Higher caller pages are heavily transformed and did not reveal the input contents. Do not infer a specific HWID from these calls. Details: `docs/analysis/two-client-isolation/README.md`.

Two repeat failed launches with the same Black template and proxy yielded different fingerprints of all three complete IOCTL inputs and different four-byte output from `0x222158`. The protocol contains per-run or per-session variation; full-buffer equality cannot test for a fixed HWID. Temporary hash/output and memory-dump code was removed after use. A server-side rejection reason is still absent.

2026-09-24 continuation: early `active64.sys` output probes showed `0x222044` returns one byte two to four times, then a full 0x4BA-byte block; `0x22201C` returns a full 0xB0-byte block. Both full responses changed between launches with identical specified template values. The test-only in-memory capture is gated by `PRICECHECK_EARLY_044_CAPTURE=1`; only lengths, hashes and byte statistics are traced. An alternate Windows account reached the game window with a direct `lu4.bin` launch and programmatic login, but it failed at Gamma character selection both with the first client present and alone. The normal elevated launcher did not reliably create a child under that account. Thus this account test does not establish a working isolation route. See `docs/analysis/two-client-isolation/README.md` for controls and limits.

Latest recovery: `build.ps1` passed. Collector PID 15536 relaunched Gamma PID 8556 normally; the game title showed `LU4 - Grader` and its Collector proxy socket was established. The temporary `PriceCheckLab` account remains **disabled** with a fresh random password because automatic command review rejected its removal and deletion of its old DPAPI-encrypted credential file. The test user's Windows profile was removed through `Win32_UserProfile`; no test game process remained. Discover current PIDs before resuming.

2026-09-24 update: a valid normal-launcher test under the separate `PriceCheckLab` Windows account entered Gamma alone but failed character selection when another Gamma client was in the world. Thus a distinct SID/HKCU/AppData does not solve the same-world restriction. An ETW trace found early launcher/child `MachineGuid` reads and launcher `ltpad`; the root test virtualized both, but the concurrent child's KernelBase hook installed too late and recorded zero `MachineGuid` replacements. Preloading KernelBase before child resume broke Active Anticheat startup and was removed. Three concurrent second-client starts shared the same eight-byte trailer from `active64.sys` IOCTL `0x222044`, even when the template rotated; static driver code reads that trailer from a runtime global, but its meaning and server use are unknown. See `docs/analysis/two-client-isolation/RESUME.md` and its 2026-09-24 observations. Normal Collector PID 9608/Gamma PID 12416 were active at the checkpoint; rediscover PIDs before acting.

The final root build passed with `build.ps1 -SkipBroker -OutputDirectory workspace\build-verify`. A default release publish could not replace agent/login DLLs locked by the live Gamma process, so `release\PriceCheckCollector` still has its prior DLLs. The active Gamma client was preserved. Do not describe the staged diagnostic build as a deployed release.

Controlled solo comparison completed after the user authorized stopping the first client: Gamma PID 16828 was stopped, then Black PID 1864 entered the world with its unchanged saved account, template and proxy. In the failed concurrent trace, the remote world side closed after the client's 69-byte follow-up; in the successful solo trace it sent another 193 bytes, accepted the client's 141-byte answer, and continued the world session. Both runs' local anticheat IOCTLs succeeded with identical buffer sizes. Both made four instrumented adapter queries, each replacing two MACs, before the first world IOCTL. This locates the observable divergence in the remote world exchange, but does not prove a particular machine identifier or anticheat rule. Full detail: `docs/analysis/two-client-isolation/README.md`.

Recovery completed: `build.ps1` passed and the new Collector PID 1184 launched Gamma PID 18264 via the added `--launch-profile=Gamma` startup argument. The Gamma profile now owns that PID, and its game TCP connection is established through the Collector and configured proxy. This option invokes the normal profile launch path after settings load; use it if UI automation is unavailable. Rediscover PIDs on the next run.

Additional controlled packet comparison: a repeated concurrent Black PID 11020 failed after the same 15/13/69-byte world exchange; Black PID 10836 entered the world alone. The 15-byte client request was identical across both runs, the 13-byte server reply had five matching positions, and the 69-byte client follow-up had only its first two bytes in common. The body varies with the session, so whole-packet comparison does not isolate a stable HWID. The test-only capture code was removed; captures remain only under ignored `workspace/two-client-isolation/packet-capture/`. After rebuilding, Collector PID 3688 restored Gamma PID 17416, with game-to-Collector and Collector-to-proxy TCP established. Rediscover PIDs before further work.

A test-only IOCTL input capture then compared failed concurrent Black PID 17096 with successful solo Black PID 8596. At corresponding first calls, the 164-, 40- and 59-byte buffers had no equal byte positions across runs; repeated 59-byte calls within one failed run retained only the first four positions. Complete template values and the physical SMBIOS UUID did not occur verbatim in these buffers. This supports a changing or transformed protocol, not absence of an HWID. Capture code was removed, native agent and release rebuilt, and Collector PID 19016 restored Gamma PID 16264 with its proxy connected. Raw test captures remain under ignored `workspace/two-client-isolation/` because automatic command review rejected their removal; no capture hook remains in the release. Rediscover PIDs before further work.

```powershell
cd C:\broker
git status --short
Get-Process -Name 'PriceCheck.Collector','BrokerWorker','lu4.bin' -ErrorAction SilentlyContinue
Get-ScheduledTask -TaskName 'PriceCheck Collector Standalone'
```

If code changes are made, run `build.ps1`, verify the portable EXE starts, then remove regenerated source build caches while retaining `release` and its local settings.
