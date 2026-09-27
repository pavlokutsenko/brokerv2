# Broker runtime failures, 2026-09-25

## Confirmed cause

The user's 17:41 / 17:43 failures were real `discover_unreal_globals.py`
exceptions, not a broker response timeout. On live PID 19292, GObjects at
module RVA `0x80768E0` was valid (337263 entries, six chunks), but the first
slot of its last chunk was empty after GC. The old validator rejected that
normal sparse slot and repeated a 11.5-million-slot discovery scan.

The supported PE build (`0x956E0D97`, image size `0xDCEB000`) now uses direct
module-relative globals plus structural, internal-index and name guards.
The last dynamic slot no longer determines validity. Unknown builds still
fail closed; broad discovery requires explicit `--scan`. A read-only live
control on the same process passed in 1.786 ms, zero scanned slots.

Artifacts: `workspace/broker-current-probe.json`, `workspace/broker-globals-fixed.json`.
The sparse last chunk recurred on new PID 7928 during testing and remained valid.

## Additional fixes

- Broker errors retry in place after 15 seconds and stop after three failures.
  They no longer provoke repeated random center movements. Full error details
  are saved under the profile's LocalAppData `runs/error-*.log`; UI gets one line.
- Unresolved identities no longer shorten the price pass. The previous one-minute
  retry left only 15–20 seconds for prices, after upload/setup and return reserve.
  Final user steering removes the broker interval entirely; complete the candidate
  pass, return to center, then immediately collect the broker again.
- Names are captured from guarded live actors immediately before queries and
  again after them, within this unique pass and PID. This handles a shop closing
  or reopening while its item queries are in progress. Old `latest_actor_snapshot`
  files are no longer used for raw broker name enrichment.
- The pre-pass map admits only ObjectIDs actually returned by the broker. PID,
  module base and capture/start time guards reject old maps. Complete absence
  remains forbidden if any identity is still unresolved or the center guard fails.
- A per-PID / process-start exclusive file handle protects the radar reader
  across collector processes. It allows different clients and is released by
  Windows after a crash. Before this fix, two reader instances could attach to
  one receive hook and interfere with each other's cleanup.
- Explicit `--collect-profile=<profile GUID>` starts the usual reader/cycle
  after settings and optional `--launch-profile=Gamma`. Normal startup remains
  stopped. This enables a repeatable desktop test without unreliable UI clicks.

## Live tests before final desktop deployment

`tests/CollectionCycle.LiveSmoke` drives the real CollectionModule on a WPF
dispatcher with real driver, packaged worker, leases and uploads. It does not
launch/terminate the game. It must run with the desktop reader disconnected.
Only new captures are uploaded; the previously blocked saved export is untouched.

PID 7928 / `workspace/broker-fix-live/cycle-1804.*`:

- First pass: 1912/1912 item replies, 1803 traders, 9276 rows, 1800 final bindings.
  Three old ObjectIDs had been replaced by new IDs for the same named shops
  (`Podavan3`, `Mir7`, `XEL`) by the time of the live control. This was a binding
  race, not ten/fourteen missing network responses. First pass remained partial.
- Second pass: 1915/1915 replies, 1801 traders, 9270 rows, 1801 bindings. Complete
  epoch reached the server; active count became 1801.
- Two tours read 14 and 16 shops. The first ended at its old short duration cap;
  the second completed. Server checked counter rose from 50 to 70 (some reads
  may be historical-only or existing/fresh-invalidated work; not equal to reads).
  Both outboxes drained without rejected files. Capture-ID comparison in PostgreSQL
  confirmed all 22 exact snapshots received and current; eight other reads had an
  unsupported exact-price format and were not published as verified prices.
- The harness stopped normally, detached the reader and left game/launcher alive.

Raw runs: profile `6a0358a7984d4ef18570283b79c3cf88/runs` beneath LocalAppData
PriceCheckCollector/collection. First/second broker filenames:
`broker-c18dcffdb9a041fd9a60cb8d73f3d906.json` and
`broker-2e2bcc3fcba34d20a0e0745c3b6869c6.json`.

## Test-operation incidents

UI capture/click tool failed with unavailable geometry. A keyboard system-menu
attempt accidentally closed collector 18916 and its game 19292. This was disclosed
to the user; normal launch restored Gamma as PID 7928. Do not repeat blind menu
navigation. Later the user started the desktop while the first short harness
was active; movement ownership rejected overlap. Both were stopped, the user
disconnected the desktop reader and explicitly asked us to run the tests.

## Verification

Latest user steering: attempt all due candidates from one broker observation
before returning, not just a short batch or a five-minute tour. Implemented a
finite in-memory candidate set; successful/failed/unavailable candidates leave
this pass, untouched candidates remain for subsequent batches. Server scheduling
remains authoritative. There is no broker timer or interval setting. Exhaustion
returns to center; after the three-second settling guard the next broker starts.

Found a second early-return cause during desktop testing: a new claim while the
previous batch's background results were pending returned `jobs:[]`. The API now
returns `awaitingPreviousBatch:true`; the collector waits rather than treating
that as market exhaustion. Isolated server and coordinator regressions cover this.

Desktop PID 8040 (before the final whole-pass change) completed 1913/1913 broker
queries. Fresh pre-pass bindings recovered two identities lost before the final
snapshot (1809 combined vs 1807 final bindings); four remained unresolved, so
absence stayed non-authoritative. Consecutive price batches ran without center
returns until the old empty-claim behavior was encountered. Do not claim binding
races have been eliminated or every pass must be complete.

21 Python tests (globals, binder guards, send wrapper, session cache), module
isolation including bounded broker retries and durable diagnostics, and the
cross-process reader ownership smoke passed. Full `build.ps1` staged publish
passed. The coordinator regression also verifies exhaustion, return, settling and
the next broker within six seconds even with a legacy 30-minute profile interval.

Final no-timer release published via `build.ps1 -SkipBroker` at 18:35 and launched
normally with `--launch-profile=Gamma --collect-profile=6a0358a7-984d-4ef1-8570-283b79c3cf88`.
Collector PID 16024, game PID 15232 (rediscover after restart). Live UI confirms
the cycle explanation and no interval field. Source/release Collection DLL hashes
match. An idle uploader briefly held the old DLL after desktop shutdown; after
the outbox drained the verified uploader was stopped and publish succeeded.

The preceding desktop PID 12844 completed six consecutive price batches from
18:29:05 to 18:32:37 without any center/broker interlude: 117 raw shop captures,
92 exact-price captures. Seventh batch was cancelled normally for deployment.
Server checked counter reached 288. This proves batch continuation, not exhaustion
of the entire market. New PID 15232 live continuation is being verified.

Autostart can attempt the first center route before the world capsule is ready:
the strict 9 x 23 capsule guard rejects it. This occurred on PIDs 8040, 12844 and
15232; the normal 30-second retry recovered. The guard was not weakened. This
startup readiness issue is separate from the removed broker interval.
