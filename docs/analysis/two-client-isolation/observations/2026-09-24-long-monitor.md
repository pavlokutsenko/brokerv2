# Long paired observation — successful observed interval, stopped by user

## Final result — 2026-09-24

**Success for the observed local configuration:** two separate profiles remained
connected to Gamma on this PC throughout the sampled interval, with no newly
recorded world close/EOF. The user reported that everything appeared to work and
requested stopping the monitor. This closes this observation successfully;
the originally planned 24 hours were not completed.

| Measurement | Final result |
| --- | --- |
| First / last sample | 2026-09-24 09:35:28–12:13:48 UTC (12:35:28–15:13:48 Kyiv) |
| Sampled duration | 2 h 38 min 20 s |
| Graceful monitor stop | 12:13:58 UTC; `phase=stopped`, monitor process exited |
| Checks | 951 paired samples, 914 fully `healthy` |
| Remaining samples | 35 skipped coordinate checks during the repaired profile-binding reset; 2 one-sample metadata backlogs |
| Sampling gaps | 0; maximum step 10 seconds |
| Newly recorded world close/EOF | 0, excluding historical baseline data |
| Process mismatch / absent established TCP / closed tunnel samples | 0 / 0 / 0 |
| Available new IOCTL failures | 0; both trace caps were reached, so later IOCTL errors cannot be excluded |
| Monitor stderr | Empty |
| Automation `gamma` | `PAUSED`, other saved fields preserved |

At **12:14:46 UTC**, after stopping the monitor, both original client PID/start
times, unique profile-ID bindings and established TCP connections were checked
again and matched. Games, proxies and drivers were left running. Only the
monitor's `stop.request` and private review/summary files were written during
shutdown; client settings were not changed in this shutdown operation.

The 35 position checks missing from 11:37:58 through 11:43:38 UTC remain a
partial coverage gap, even though `gap_count=0`: TCP, original process identity
and fresh traffic were still observed. They are not counted as fully healthy
or treated as a game disconnect. The two short metadata backlogs also remain
in the recorded counts. Details below and in the module-separation note.

Private `final-summary.json` contains the aggregate and post-stop checks;
`followup-state.json` records terminal review state. The event cursor ends at
sequence 7. The scripts/automation must not restart this run automatically.

Scope: this success belongs to the then-running pre-refactor release, current
client version, two accounts, fixed generated identities and restored original
proxies. It does not establish the reason for the earlier rejection or a
periodic anticheat schedule. The newer two-module build, another physical PC
and future game updates still require their own live validation.

The sections below preserve the chronological launch and review record.

The user suspects a same-world two-client check every few hours and asked to
start monitoring. This is a hypothesis; no server/anticheat reason code or
schedule has been established. The prior matched 420-second solo and
602-second paired controls are documented separately. This continuation
observes the already running pair without changing the experimental inputs.

## Launch and verification

- Start: 2026-09-24 **09:35:28 UTC**; deadline: 2026-09-25 **09:35:28 UTC**.
- PowerShell monitor PID 19880, process start 09:35:27.927 UTC.
- Gamma PID 19660 (start 09:10:43 UTC), Black PID 14980 (09:11:16 UTC),
  desktop Collector PID 18620. PIDs were revalidated before attaching.
- Sampling target: 10 seconds for 24 hours. The monitor is a separate hidden
  PowerShell 7 process, so the interactive command completed while it stayed alive.
- At 09:36:28 UTC: 7 samples, all 7 healthy, maximum gap 10 seconds,
  one baseline event, no observation gaps, no stderr. Both clients retained
  established TCP, valid coordinates and fresh traffic.
- Native/runtime/settings remain as in HANDOFF; no new game launch, driver
  load, memory/packet modification, HWID rotation or proxy change in this test.

Private output: `%LOCALAPPDATA%\PriceCheckCollector\research\long-monitor\20260924-123527`.
`current.json` in the parent points to it. `status.json` records the monitor PID
and process start time, deadline, sample counts, last sample and terminal phase.
All samples append to `samples.jsonl`; state changes, world EOF, available IOCTL
errors and sample gaps append to `events.jsonl`. Event folders retain the last
2000 lines of each client's known proxy/IOCTL metadata logs. The complete
original metadata logs continue under LocalAppData `logs`.

Heartbeat automation **gamma**, “Наблюдение двух окон Gamma”, was created for
this existing task every 15 minutes. It checks freshness and process identity,
keeps its review cursor in the private run directory, remains quiet while
nothing actionable changes, and reports new failures/recovery/completion.
It must pause itself after completion or final monitor termination. This
follow-up is separate from the script's 10-second collection interval.

## What the monitor can establish

It pins the initial profile ID, PID and process start time. A restarted client,
changed profile binding or reused PID is flagged rather than silently followed.
Each sample combines live OS TCP state, bounded read-only coordinate observation,
world tunnel state and recent traffic. Valid coordinates alone are insufficient.
An 8-second timeout bounds the Python observer. Temporary observation errors
are recorded without stopping the entire run. A local mutex prevents duplicate
long monitors, and `stop.request` ends monitoring without stopping the games.

The metadata reader handles partial CSV lines and truncation. The first read
includes historical log contents and is marked `baseline`; old login EOF is
not a newly observed world failure. Subsequent reads are incremental. A change
or close takes a metadata snapshot. Absence of traffic for 90 seconds is
`traffic_quiet`, not a claimed server decision. Sampling gaps over 45 seconds
(or three configured intervals) are explicit evidence gaps. Sleep/offline time
must not be reported as successfully observed game uptime.

Lengths in proxy logs are transport read chunks, not necessarily protocol
message boundaries. IOCTL tracing selects only the existing traced codes and
stops after **4096 events** per client (`native/ClientLaunch/trace.cpp`). The
monitor marks `cap_reached`; it cannot observe new IOCTL failures after this
cap. TCP metadata and OS checks remain available. A later EOF is evidence of
transport closure, not by itself the reason, a ban or a periodic two-window
check. This passive paired observation is not an additional matched solo test.

## Verification of the observer

`tests/TwoClientIsolation.Monitor.Smoke.ps1` passed synthetic CONNECT, historical
login EOF exclusion, partial read completion, world EOF, duplicate prevention,
log truncation and failed-IOCTL error-code cases. A live `-Once` then found both
clients healthy, followed by the detached monitor's seven successive checks.
Root `build.ps1 -SkipBroker -OutputDirectory workspace/build-verify` passed;
the active release was not overwritten.

The first live smoke incorrectly classified the world tunnel as closed while
TCP and fresh traffic remained present. Its CSV event-name regex excluded
digits in `proxy_http_200`; corrected before the long run and covered by the
synthetic test. This was an observer bug, **not a reproduced game disconnect**.
The unsuccessful smoke output remains private in `long-monitor/20260924-123345-512`.

At launch, results were pending beyond the initial interval. The final result
above now closes the run; initial success was not extrapolated to a full day.
No secrets or raw packets belong in this document or repository.

## First scheduled review, 09:51 UTC

At 09:51:38 UTC the monitor had 98/98 healthy paired samples, no observation
gaps (maximum step 10 seconds), only the original baseline event and empty
stderr. Its live PID and exact process start time matched the manifest. Both
clients' PID/start times still matched their initial processes and the saved
profile bindings; an independent OS query found established TCP for each.
Recent sends and receives continued. Review cursor was saved privately in
`followup-state.json`; no new actionable event required a notification.

Gamma's IOCTL log first showed its 4096-event cap in the **09:50:28 UTC**
sample. Black had not reached the cap at the 09:51:38 snapshot. This is the
known tracing limit, not a game disconnect or an observation gap in TCP.
Later Gamma IOCTL errors cannot be excluded using this capped log. The
24-hour paired observation remains in progress.

## Scheduled review, 10:06 UTC

At 10:06:38 UTC: 188/188 healthy samples, no gaps, maximum step 10 seconds,
no events beyond the baseline and empty stderr. The monitor and both client
PID/start times matched; saved profile bindings and independent established
TCP checks also passed. Recent bidirectional traffic continued. No new
actionable event or user notification; private review cursor advanced.

Black first reached the IOCTL trace cap at **09:53:28 UTC**, so both native
IOCTL logs are now capped. Later failures through that interface cannot be
excluded from these logs. TCP metadata and live process/connection checks
continue. No delayed disconnect has been observed in the monitored interval;
the cause and existence of a periodic same-machine check remain unproven.

## First-hour checkpoint, 10:41 UTC

At 10:41:38 UTC: 398/398 healthy paired samples across approximately 1 hour
6 minutes of this monitor, no gaps, maximum sampling step 10 seconds and no
events beyond the initial baseline. Monitor and client PID/start times and
saved profile bindings matched; independent OS TCP checks found one established
connection per client, with recent traffic in both directions. Both IOCTL
logs remain capped as previously documented. No disconnect was observed during
this sampled interval. The 24-hour observation continues; unchanged health
did not trigger a user notification.

## Transient metadata backlog reviewed, 10:56 UTC

Event 2 at **10:55:38 UTC** marked Black `metadata_backlog`: 53 bytes were
pending at the end of an incremental log read. Both processes, coordinate
checks, world tunnel flags and established TCP remained valid, fresh traffic
continued, and no close/EOF was recorded. Event 3 at **10:55:48 UTC** returned
to healthy with zero backlog. Both saved event snapshots were checked.
The reader samples file length again after reading, so concurrent appends
can appear as this small pending tail. These events do not demonstrate a
disconnect or an anticheat check. They are retained as recorded, including
the one non-healthy sample; no history was rewritten.

At 10:56:48 UTC the run had **488 healthy samples out of 489**, no sampling
gaps, maximum step 10 seconds, fresh status and empty stderr. Original
monitor/client process identities and independent TCP checks still matched.
Both IOCTL logs remain capped. This short metadata delay did not warrant a
failure notification or a recovery notification for an unreported problem.

The saved configuration now also contains an additional unbound Gamma and
an unbound White profile. A review query by display name alone therefore
reported an ambiguous Gamma match. The original Gamma and Black still each
uniquely own their original PID; the monitor's checks against its pinned
profile IDs also passed. Those profile IDs were added to the private review
cursor for subsequent independent checks. This is not a change to the live
pair's binding. No profiles, client settings or monitor code were modified.
## Дополнение 2026-09-24, 11:44 UTC

При рефакторинге модулей первый UI smoke неожиданно запустил production WPF
startup и пересохранил профили, обнулив старые PID без start time. Event 6:
11:37:58 UTC, обе привязки не совпадают. После проверки оригинальных ID/PID/
start time восстановлены только привязки; event 7: 11:43:48 UTC, healthy.
35 samples между ними пропускают координаты, но во всех сохранены matching
processes, established TCP и свежий обмен: max rx age 4 s, tx 30 s, EOF 0.
Учитывать частичный пробел проверки позиций отдельно от `gap_count=0`
(этот счётчик относится к отсутствию samples). Не считать разрывом игры.
См. [разбор и исправление теста](../../module-separation/README.md).
