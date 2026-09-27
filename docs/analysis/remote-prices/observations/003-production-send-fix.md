# Production send compatibility fix — 2026-09-24

User explicitly requested promoting the successful control fix into the working
collector. This changes direct-sender installation compatibility; it does not
repair native HUD ProcessEvent selection or provide remote no-target prices.

## Change

`tools/BrokerWorker/src/client/send_prologue.py` resolves three reviewed cases:
the unhooked five-byte send prologue, the existing retained-prologue relay, and
that relay wrapped by the current ClientAgent MinHook. The controller uses the
returned prologue and records send_mode/send_relays in PID-scoped state.

The wrapped case validates the loaded module's callback RVA 28C70, exact
instructions at RVA 28D6C, the RIP-relative original-send slot (currently FDE18),
the MinHook absolute jump, and the prior relay's original instruction and return
to send+5. It follows one known wrapper only. Changed callbacks, instructions,
cyclic/empty pointers and mismatched return sites are rejected before patching.
No runtime name scan, arbitrary jump-chain following or native DLL change.

## Validation

- Five unit tests passed, including eight damaged/unknown-chain subcases.
- Root `build.ps1 -ForceBroker -OutputDirectory workspace/target-send-fix-publish`
  passed and rebuilt the frozen worker with the new helper.
- Read-only resolution on live PID 19660 returned
  `recovered-prologue-from-agent-relay`, three relay addresses.
- Newly built **BrokerWorker.exe** completed normal price-prepare and price
  against MHE/ObjectID 1247840039. Two ordinary target actions yielded fresh
  shop capture sequence 1 with three rows. Total 1381.658 ms, cleanup target 0.
  Ordinary approach from about 206 units was permitted by the control.
- Both installed BrokerWorker executables independently loaded the updated
  scripts and passed install/status/uninstall against PID 19660, sequentially.
  Direct/post signatures matched while installed and restored afterward;
  ProcessEvent restoration also checked. Client remained responsive.

## Installed delivery

Updated only `_internal/client/send_prologue.py` and
`_internal/client/lu4_target_controller.py` under both running hosts:

- `C:/broker/release/PriceCheckCollector/BrokerRuntime`
- `C:/broker/release/packages/PriceCheckCollector-win-x64-20260924-132255/PriceCheckCollector/BrokerRuntime`

No BrokerWorker was active during replacement. The helper was placed first,
then each controller file was atomically replaced. File hashes were verified.
Worker executables and modification stamps were preserved; the helpers are
dynamically loaded on each invocation. No host/game restart, native deployment,
managed assembly replacement, cache copying, profile edits or active-state
deletion. Neither host had existing runtime-sessions copies. Future sessions
inherit the updated source runtime. The older distribution ZIP was not
rewritten; both extracted running installations and the staged build are fixed.

Rollback copies and per-file hash manifest:
`%LOCALAPPDATA%/PriceCheckCollector/research/send-fix-deploy/20260924-164217/manifest.json`.
With worker jobs stopped, restore entries whose existed is true from their
recorded backup; remove only the new helper where existed is false. Keep
runtime state/session JSON and worker binary stamps untouched.

Artifacts in the PID 19660 research directory: production-prepare.log,
production-hook-validated.json, production-price-MHE.json, production-cleanup.log,
installed-worker-validation.json, production-fix-final.json. Final observed
target 0 and position (15722.640011146792,143102.10507731125,-2713.715455444486).
