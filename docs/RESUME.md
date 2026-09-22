# PriceCheck collector — continuation checkpoint

Updated: 2026-09-23 (Europe/Kiev)

## Start here

- Repository: `C:\broker`
- Read `AGENTS.md`, this file, then `docs/PRICE_VERIFIER.md`.
- Branch: `main`.
- Portable application: `C:\broker\release\PriceCheckCollector\PriceCheck.Collector.exe`.
- Normal development cycle: `cd C:\broker; .\dev.ps1 run`.
- Full packaging check: `cd C:\broker; .\dev.ps1 full`.
- Scheduled task `PriceCheck Collector Standalone` points to the portable EXE in `C:\broker` and is currently running.
- The market API and website are separate in `C:\Users\Pavel\Documents\ChatGPT\pricecheck-market`; their scheduled tasks and paths were not moved.

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

```powershell
cd C:\broker
git status --short
Get-Process -Name 'PriceCheck.Collector','BrokerWorker','lu4.bin' -ErrorAction SilentlyContinue
Get-ScheduledTask -TaskName 'PriceCheck Collector Standalone'
```

If code changes are made, run `build.ps1`, verify the portable EXE starts, then remove regenerated source build caches while retaining `release` and its local settings.
