# Gamma three-account live run, 2026-09-29

## Setup and observation

Collector PID 12356 owned three Gamma clients: 14856, 7844 and 18232. All
three reader agents reported `ready` and applied world identity. Collection
started at 19:52 local time. The first broker pass returned 1,736 traders and
1,898/1,898 item replies, then formed a shared route of 1,613 targets.

The live log showed repeated turn handoffs 14856 -> 7844 -> 18232 -> 14856,
with the same route ID `b9bd4225579a4d6b97a56f5ecb661964`. Exact shop
snapshots from 14856 and 18232 received individual server acceptance. PID
7844 repeatedly failed before navigation with `wait_navigation_capsule`
observing effective size `(7.5, 23.0)`. The collector continued handing off
after these failed sections, but no shop was read on that client.

## Capsule validation

The read-only `inspect_navigation_capsule.py` diagnostic initially rejected
the updated game image because it used fixed old-build globals. It now uses
the same per-image validated world resolver as navigation. On all three live
PIDs it verified the pawn `CapsuleComponent` class and current build
`timestamp=2745613103`, `image_size=255692800`:

- 14856: radius 5, half-height 19, scale (1,1,1)
- 7844: radius 7.5, half-height 23, scale (1,1,1)
- 18232: radius 9, half-height 18, scale (1,1,1)

The 7.5x23 shape is narrower than the already accepted 9x23 map envelope at
the same height. Runtime navigation still checks the live pawn capsule and
uses native `Pawn` collision sweeps before each move. The exact 7.5x23 shape
was therefore added to the allowlist. The previously observed 7.5x24 shape
remains rejected until separately validated.

## Fixed live check

`build.ps1` rebuilt the broker runtime and published a test Collector. The
old Collector closed through its normal WPF shutdown, stopping its three owned
clients. A new explicit `--launch-accounts=Gamma` startup switch launched the
configured accounts sequentially with their individual templates; the
existing `--collect-profile=<Gamma GUID>` switch started collection afterward.

The replacement Collector PID 18156 owns Gamma PIDs 18196, 18456 and 19852.
The 7.5x23 client is PID 18456. Its exact reads at 20:16:40, 20:19:36 and
20:20:18 local time each produced a durable snapshot and an individual server
acceptance. The log repeatedly shows 18196 -> 18456 -> 19852 -> 18196 with
one shared route ID `35d492ef0f7548938a3b5d4c659645a7`. A live process
snapshot showed one BrokerWorker process across the three clients. In the
seven-minute sample ending near 20:21, 15 individual snapshots were accepted
with zero ERROR and WARNING journal events. The three clients and collection
were left running at that checkpoint; later controlled restarts replaced them
with newer builds for shared-count and lookahead validation.

The focused `test_capsule_profile.py` suite passed all four tests and
`build.ps1` completed. A natural 60-minute character rotation was not observed
in this short run.

## Shared counter and waiting-account lookahead

The UI now takes pass counters and the visible queue from the market's shared
`LocalCycleStore`, regardless of the active PID. Before each trader, the
waiting account plans the predicted next target from its own position in a
separate offline `market-plan` worker. The completed plan is handed to the
next native route, whose map, target revision, and current-position checks
remain authoritative. ObjectID is excluded from the geometry fingerprint;
the active reader still binds a fresh process-local ObjectID for exact reads.

The first live attempt incorrectly required a recent packet timestamp on the
waiting radar. A stationary client can have a fresh direct-memory position
but an old last-packet time, so the packet-age gate was removed. Route
preparation is also triggered immediately when the active trader is selected,
with the UI timer retained as a fallback.

The validated pipeline Collector PID 15496 owned Gamma clients 18996 (Loriqq), 14184 (Wics),
and 5052 (Uce). Broker completed with 1,773 traders and 1,920/1,920 replies,
then created one route ID `0b94b4e137c94a04a96de5a0d1c9107a` and 1,579
targets. At 21:32:37 local time PID 14184 began offline planning while PID
18996 read Heiko; the plan finished at 21:32:42 and the turn moved at 21:33:00.
At 21:33:02 PID 5052 planned xMILLIONAREx while PID 14184 read SamaraTrade.
The third account's route input contained the prepared 21-point path, and its
motion log recorded `background_plan=true` and `background_geometry=true`.
Heiko and SamaraTrade both produced exact snapshots accepted individually by
the server. xMILLIONAREx was read exactly, but the server retained that receipt
as historical pending verification; the durable local snapshot remains.

`LocalCycle.Smoke` passed 34 main checks plus protocol/durability suites,
`ModuleIsolation.Smoke --market-turns-only` passed the shared pass, counter,
lookahead, and fresh-binding checks, and `--route-continuation-only` passed
after correcting its fixture order. `CharacterRotation.UiSmoke` passed after
correcting its text selector. Ten focused Python route/capsule tests passed.
The general `ModuleIsolation.Smoke` suite still has a separate synthetic
broker fixture using an unreachable server URL; it was not relied upon for
this live validation. Natural 60-minute character rotation remains unobserved.

One final retry fix makes a cancelled or timed-out offline planner eligible
to run again; its focused test passed. `build.ps1` published this version to
`release/PriceCheckCollector` and `release/PriceCheckLauncher`. The final
Collector PID 11128 was started from that release and owns the three responsive
Gamma windows 18044 (Loriqq), 14112 (Wics), and 14276 (Uce). Collection was
enabled and the first broker pass began on PID 18044 at 21:45:32 local time.
These windows were left running.

## Shared market display and turn-start diagnosis

The next live session showed that the old collection header mixed a complete
center scan with the active PID's small roaming radar and last broker object.
Its "current price" figure was local store eligibility, not a server count.
The collection view now uses the latest complete native center scan for both
the shared count and map, retains the shared broker-delivery counter, and
derives the server-current-price count from the complete server roster. A
price counts only for an active trading kiosk with a matching read type, a
read after the server's verification requirement, and a read within the
profile's recheck interval. The roster is refreshed in the background once
per minute, with its capture time displayed. Per-PID roaming and broker
counters were removed from this market-level header.

Collector 11128 continued handing off between its three PIDs. In 77 route
sections after startup, preparation averaged 5.6 seconds and completed in
about 2-3 seconds when an accepted background plan was available. Two first
sections on waiting PIDs took 111-113 seconds, including one with an accepted
background plan. This rules out the offline path calculation alone as the
cause. The route worker now records separate reader startup, native install,
navigation geometry, and shop reader setup timings for the next cold run.

At 22:13:42 local time, a route-session finish command hit a sharing violation
on its unique `.command.json.tmp` file. The command writer now retries a
bounded transient sharing violation, and a market turn cannot transfer while
its native route session remains owned. If the finish command still cannot be
published, the collector waits for the worker's bounded idle exit rather than
reporting native cleanup as complete. At 22:14:06 the old Uce PID also hit a
game-thread ProcessEvent timeout during native cleanup; automatic recovery
replaced it, then the old collector was stopped normally for release update.

The updated collector release was started at 22:18:37 local time as PID 6164.
Its three Gamma clients are 9624 (Loriqq), 20404 (Wics), and 2388 (Uce).
The first broker began at 22:21:39; its cold-section timings are recorded
below.

The instrumented run confirmed the delay: Wics' first route spent 129.16 s
in `shop_reader_setup` and only 0.66 s restoring planned geometry plus 0.27 s
installing movement ownership. Uce spent 129.62 s in `shop_reader_setup`,
with 0.12 s geometry and 0.27 s native installation. The read-only connection
resolver inside `ShopHooks.prepare` performs that once-per-PID scan; later
turns reused the cache and the 9624 -> 20404 -> 2388 handoffs resumed normally.
The first broker was incomplete due to three unbound traders (19 rows), but
1938/1938 item replies arrived and the independent center radar was complete;
its safe data and route continued. No shared-pass reset was observed.

A new `market-reader-prepare` worker mode runs only the PID-bound read-only
connection and active64-state cache preparation. The UI schedules it for both
waiting accounts as soon as market collection starts, while the first broker
is still active. Each cache is validated again before use by the native route.
The handoff waits for any still-running preparation of its next PID, avoiding
two writers for that PID's cache. A stop marker cancels child diagnostic
processes on collector stop or close. A packaged-worker smoke on an already
warm PID 20404 returned `{ready:true}` in under a second. The next cold launch
began at 22:31:53 as collector PID 9640; its first broker and handoff timings
are recorded below.

The final cold run owned 3028 (Loriqq), 17324 (Wics), and 19852 (Uce).
Both waiting readers began read-only preparation at 22:34:40. Uce finished
at 22:36:16 and Wics at 22:36:45, while the one active account ran the
broker. The broker completed at 22:38:02 with 1823 traders and 1962/1962
item replies; the separate complete center radar saw 2114 identities. The
first price section then passed 3028 -> 17324 -> 19852 -> 3028. Their first
preparation times were 7.19, 3.39, and 1.84 seconds respectively; Wics' and
Uce's `shop_reader_setup` steps were only 2.36 and 1.43 seconds, down from
129.16 and 129.62 seconds without prewarming. The next two-minute sample
had 11 handoffs, seven server-accepted individual prices, zero errors and
zero warnings. All three game windows were responsive and collection was
left running. A read-only complete server roster at 22:40:53 contained 1834
rows, of which 310 active trading shops met the server-history/current-price
predicate for Gamma's 24-hour interval.

The final release also guards against a refresh-timer relaunch of reader
preparation during a manual collection stop. It was installed after the old
upload helper exited naturally and launched as collector PID 19976 at
22:45:04. Its three responsive clients are 17500 (Loriqq), 14648 (Wics),
and 18728 (Uce). Both read-only warmups started at 22:47:50 and finished at
22:49:21/22:49:48. Broker completed at 22:51:13 with 1954/1954 replies and
a complete center radar; one unbound trader (five rows) marked the inventory
incomplete, so safe data was retained and the route continued. First-section
preparation took 8.06, 3.53, and 2.72 seconds respectively. The shared pass
then handed off 17500 -> 14648 -> 18728 -> 17500, with individual exact
prices accepted by the server. The final release and all three windows remain
running.
