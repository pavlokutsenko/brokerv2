> Historical architecture. V67 uses local SQLite scheduling; see [LOCAL_CYCLE_V67_20260926.md](LOCAL_CYCLE_V67_20260926.md). Broker absence never removes shops.

# Server-owned price schedule and power-loss recovery

2026-09-25. Supersedes the earlier local CycleQueue design.

Follow-up: finite whole-market visits and broker runtime fixes are described in
[BROKER_RUNTIME_FIX.md](BROKER_RUNTIME_FIX.md). The active pass keeps only an
in-memory candidate set from its broker observation. It does not persist or own
deadlines/price status. `awaitingPreviousBatch` keeps this set intact while the
server finishes previous uploads/releases. Batch boundaries do not interrupt a
market pass. There is no broker interval: exhaustion returns to center, settles
for three seconds and immediately begins the next broker observation.

## Ownership

- PostgreSQL in C:/broker-server owns pending checks, revisions, 24-hour due
  times, transient retries (2/5/15 minutes), leases and completion.
- Every collection start has a random worker identity in addition to profile
  identity. Cloning a profile onto another PC cannot clone an active lease.
- Local configuration contains the center, market, API address and launch
  settings. Runtime bindings contain only the current client's ObjectIDs and
  coordinates. An ObjectID from another computer is never actionable.
- Local queue.json is no longer read by the active cycle. Historical CycleQueue
  helpers remain only for old policy tests; they do not schedule work.
- The local outbox contains observations awaiting server acknowledgement, not
  the market schedule. Flushed atomic writes preserve payload IDs on retry.

## Flow

1. Confirm server availability; return to a reachable random point in the saved
   center circle. Collect the broker inventory and enrich local bindings.
2. Spool the broker epoch for background delivery with a stable batchId and the
   current worker identity. The server changes inventory and price tasks in one
   market transaction. Quantity-only changes leave the price deadline intact.
3. Claim up to 500 locally known targets only after this broker batch is committed.
   Claims use the same market/city transaction lock as broker/price ingestion.
   Replaying a claim request returns the same jobs.
4. Build the existing smooth route with leased targets and local binding guards.
   Renew two-minute leases every 25 seconds. Loss of ownership/connectivity
   requests a normal route STOP and native cleanup. The operator can restart
   after a lease stop. F8/STOP remain supported.
5. Spool complete exact price snapshots with lease token and revision. The
   server commits the price and completion together, scheduling the next check
   24 hours after capture (future capture time is capped to server time for the
   deadline). Keep Windows clocks synchronized for observation ordering.
6. Spool releases for unused targets and failures for attempted/blocked targets.
   Release replay is idempotent. A crashed collector's leases expire. A delayed
   result may complete an expired but unreassigned lease; it cannot clear a new
   revision or another collector's assignment. Historical prices remain saved.

Full broker absence cancels shared jobs; partial absence does not. New brokers
detect moves, additional item rows, changed type and observed reopening.
Client-local ObjectID changes are sent as reopened only after a prior binding
in that same live collection session. A different PC's ObjectID alone changes
nothing. Changes unobservable while every collector was offline remain subject
to the 24-hour check.

The WPF queue/counters now come from the server. Writing a local capture cannot
increment the confirmed-price count. Market/city scopes are independent.

## Recovery incident

After sudden power loss the old Gamma/Giran queue contained 1,126,609 zero bytes
and no usable backup. The original was preserved. New startup never parses it.
The server migration reconstructs due times from current exact price snapshots.

Settings and upload files now use write-through, Flush(true), then atomic replace.
Settings retain a backup; incomplete settings are quarantined instead of silently
overwriting configuration. Flushed pending and in-flight uploads are recovered
on worker startup. This cannot guarantee storage hardware survives a power loss,
but an incomplete local write is no longer the only scheduling record.

## Verification and deployment

- API build/typecheck passed. Isolated PostgreSQL tests passed: concurrent
  claims, token replay, foreign-owner rejection, 24h, quantity-only, shared retry,
  lease expiry/reassignment, stale revisions, partial/full absence and migration.
- C# DistributedQueue.Smoke passed against a real test API on 127.0.0.1:3022:
  background broker acknowledgement, two owners, local binding, exact int64 price
  upload and shared 24h completion. Synthetic data in a dedicated DB only.
- ModuleIsolation.Smoke: two simultaneous simulated market routes passed.
  StorageRecovery.Smoke and 23 historical MarketCycle checks passed.
- WPF render inspected; build.ps1 -SkipBroker passed. Worker/native movement code
  unchanged. No live game movement initiated for this change.
- Server migration 20260925170000_distributed_cycle_queue applied after backup
  .runtime/backups/before-distributed-queue-20260925.dump. Gamma/Giran validation:
  1,732 active, 41 checked, 1,691 pending.
- User authorized restart/relogin. Updated release published and opened as
  PID 18916 after normal shutdown of the previous collector and owned game.
  Driver running; profiles/center preserved; collection disabled. User will log
  in manually. Staged copy also remains at workspace/distributed-queue-publish.

Multiple physical PCs and two real games have not been tested. Native buffer
retention, unsupported buy/package decoding and Giran-only limits remain.
For remote PCs, configure API HOST to a private interface and use the same
reachable API address in profiles. The default remains loopback; no network or
firewall exposure was enabled. The API currently has no public-network auth.
