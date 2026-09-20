# Unified market collector

Each profile owns one game client for one market and city. Different profiles can run independently for Gamma, Black, White or Carmine. The former broker and price-verifier roles are retired; existing duplicate profiles for the same market/city are collapsed to the broker profile during migration because it contains the saved centre coordinates.

The collector keeps its price queue locally in `data/local-price-queue/<profile-id>.json`. A trader enters the high-priority queue when first observed or after an authoritative departure and return. A successful exact-price read removes that trader from the pending queue. A failed read is retried locally with bounded backoff. Broker quantity changes do not enqueue another exact-price read while the trader remains present.

Only a radar snapshot captured inside the saved centre zone is authoritative for whole-market presence. While the character roams, new observations are merged into the local catalogue, but off-radar traders are retained. Partial roaming snapshots are never uploaded as whole-market presence.

Selection follows the useful part of the old collector policy: highest-priority work first, nearest trader inside that priority, and dense local batches of up to 16 traders inside 110 world units. A local batch uses an eight-request sliding window, refilling it as ProcessEvent responses arrive, so the capture ring is not overrun by a burst. The desktop prepares the price hook once per session; hot batches never reinstall it. If no pending shop is nearby, the client follows the nearest pending trader by the normal double target action so the game performs obstacle-aware movement.

The configured broker interval is a quantity-refresh deadline measured from the end of the previous full pass. When it expires, the same client returns to the saved centre, performs one broker pass for store types 1, 3 and 8, uploads item membership and quantities, then resumes its local price route. The broker does not replace exact price/enchant snapshots and does not change local price priorities.

Radar, broker and exact-price results are written atomically to `data/server-outbox`. A separate upload-worker process sends them without blocking capture. Exact prices use `POST /ingest/price-snapshot`; they no longer claim or lease work from the server. This keeps multiple market collectors independent while the website reads the same central PostgreSQL database.

The API rejects implausible one-frame market collapses during receive-hook recovery. Present traders are still refreshed, but a cold catalogue containing zero or a handful of actors cannot mark an established market inactive.

The receive-hook radar remains the real-time source for trader arrivals, departures and movement. If the desktop restarts while the hook is still producing packets, it drains the bounded backlog and clears the old overflow counter instead of disabling collection; subsequent packets continue to update the radar normally.
