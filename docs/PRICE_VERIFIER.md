# Market collector: current and historical paths

The active recurring coordinator is implemented in [COLLECTION_CYCLE.md](COLLECTION_CYCLE.md).
See [implementation and validation](analysis/market-cycle/IMPLEMENTATION.md) and
[driver/update/concurrency audit](analysis/market-cycle/DRIVER_COMPATIBILITY.md).
It uses the manual center, broker-only admission and authoritative complete
epochs, durable LocalAppData queues, smooth flybys and 24-hour rechecks.

The sector route, radar-authority rule and app-base queue described below are
HISTORICAL. RefreshAsync no longer calls that coordinator. The native sender
and research findings remain relevant; do not infer current behavior from the
old scheduling sections.

The target sender recognizes the original send relay and the validated
ClientAgent wrapper introduced by the launcher. It checks the wrapper and
retained prologue before installing hooks; an unknown layout remains an error.
The 2026-09-24 fix passed the packaged price workflow on the live client and
was applied to both running installations without replacing session caches.
See [sender compatibility validation](analysis/remote-prices/observations/003-production-send-fix.md).

Each profile owns one game client for one market and city. Different profiles can run independently for Gamma, Black, White or Carmine. The former broker and price-verifier roles are retired; existing duplicate profiles for the same market/city are collapsed to the broker profile during migration because it contains the saved centre coordinates.

The collector keeps its price queue locally in `data/local-price-queue/<profile-id>.json`. A trader enters the high-priority queue when first observed or after an authoritative departure and return. A successful exact-price read removes that trader from the pending queue. A failed read is retried locally with bounded backoff. Broker quantity changes do not enqueue another exact-price read while the trader remains present.

Only a radar snapshot captured inside the saved centre zone is authoritative for whole-market presence. While the character roams, new observations are merged into the local catalogue, but off-radar traders are retained. Partial roaming snapshots are never uploaded as whole-market presence.

Selection follows the old collector's stable sector policy. Ordinary warm-up stays in one 420 by 420 world-unit sector until it is drained, then chooses the nearest remaining sector and the nearest trader inside it. A newly observed or reopened trader interrupts that route at higher priority. Priorities are session-local, and a failed trader is deferred for a later pass without interrupting the route. Every stop reads up to 64 pending shops from the selected sector inside the validated 95-world-unit interaction radius. The price reader keeps an eight-request sliding window, refilling it as ProcessEvent responses arrive, so a large work list does not overrun the eight-record capture ring. The desktop prepares the price hook once per session; hot batches never reinstall it. If no pending shop is nearby, the collector invokes the client's `Move to Location by Keyboard` UFunction for the selected sector anchor, delegating movement to the client. Reliable obstacle avoidance by this function has not been established; see [read-only geometry and collision findings](analysis/world-geometry/README.md).

The embedded worker validates that `latest_session.json` and `latest_active64_state.json` belong to the same PID as the live target hook before collection starts. Runtime deployment must preserve live hook state and must not copy `latest*.json` caches between client sessions; a mismatch is regenerated before the first batch.

## Manual pass-by reader

`manual-passby-shops.ps1` is the fallback mode for user-controlled movement. It force-stops only `PriceCheck.Collector.exe` so the automatic route cannot compete for the target/shop command lanes; the LU4 client remains running. The script continuously refreshes the live actor array, recomputes distances from the current player position, and sends an eight-request sliding batch for every previously unread trader entering the 95-unit radius. Successful ObjectIDs are not read again during the run. Failed reads wait 30 seconds before another attempt. There is no movement call in this mode. While the reader is active it temporarily suppresses incoming opcode `0x72`, which otherwise replaces the user's current route with the requested trader route. It also leaves target cancellation out of hot batches, because cancellation clears manual movement. The original `0x72` handler is restored in `finally` when the script stops normally or receives Ctrl+C.

Run until `Ctrl+C`:

```powershell
cd C:\broker
.\manual-passby-shops.ps1
```

For a bounded validation use `-DurationMinutes 1`. If more than one LU4 client is running, pass `-ClientPid <pid>`. Results are written incrementally to `release\PriceCheckCollector\manual-captures\*.jsonl`, with the final summary and all captured rows in the matching `.json` file. The 2026-09-21 live smoke test captured 21/21 nearby traders with zero failures; the batch itself took 2.627 seconds (7.995 shops/s).

For a distant single target, the target is not cancelled immediately after the
double action. The server-side movement order remains active until the shop
response arrives, allowing the game client to use its native obstacle-aware
pathing. The hidden target is cleared only after the price snapshot is captured.

The configured broker interval is a quantity-refresh deadline measured from the end of the previous full pass. When it expires, the same client returns to the saved centre, performs one broker pass for store types 1, 3 and 8, uploads item membership and quantities, then resumes its local price route. The broker does not replace exact price/enchant snapshots and does not change local price priorities.

Radar, broker and exact-price results are written atomically to `data/server-outbox`. A separate upload-worker process sends them without blocking capture. Exact prices use `POST /ingest/price-snapshot`; they no longer claim or lease work from the server. This keeps multiple market collectors independent while the website reads the same central PostgreSQL database.

The API rejects implausible one-frame market collapses during receive-hook recovery. Present traders are still refreshed, but a cold catalogue containing zero or a handful of actors cannot mark an established market inactive.

The receive-hook radar remains the real-time source for trader arrivals, departures and movement. If the desktop restarts while the hook is still producing packets, it drains the bounded backlog and clears the old overflow counter instead of disabling collection; subsequent packets continue to update the radar normally.
