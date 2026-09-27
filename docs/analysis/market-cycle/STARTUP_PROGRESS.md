# First-session startup, 25 September 2026

User reported a zero-counter Broker inventory screen. Live PID 22072 showed
that the worker was resolving the fresh connection, not waiting on broker:
prepare started 15:38:58; session cache written 15:40:09, encryption state
15:40:15; full raw broker finished 15:41:12 with 1,729 traders / 9,024 rows,
1,725 current bindings. Price route started 15:41:14 but repeated connection
discovery in its different research folder until 15:42:30.

Fixed:
- Per-job atomic progress file across preparation, catalogue, query batches,
  name binding and cleanup. UI uses current worker detail and elapsed time
  instead of retaining Return to center during the broker phase.
- Bidirectional cache lookup between broker runtime and route folders, limited
  to the same PID. Every candidate still passes live base, connection, wrapper,
  socket and kernel-slot validation before use; no new address assumptions.
- Fresh connection scanning observes STOP between memory chunks/regions.
- Route preparation also reports its phase before the first movement update.

ModuleIsolation.Smoke passed; BrokerWorker tests now 12 passed, including a
route consuming broker cache and rejecting it after the live socket changes.
Packaged build succeeded to workspace/progress-fix-publish and release.

User rebooted before live validation of the new UI. On restart, published the
fixed build and opened the collector, database, API and website. User will log
in the character; do not auto-launch a game or resume movement. Next check:
fresh PID progress, broker-to-price cache reuse, then actual price results.
No claim that the first unavoidable fresh-session scan is now instantaneous.
