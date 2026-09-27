# Radar pool during broker and per-pass UI (V46)

2026-09-26, Europe/Kiev. User requests: accumulate new shops during the broker
minute, show discoveries in UI, reset counters for each new whole-market pass.

## Cause and change

Previously `WriteCycleRadarTargets` returned whenever no price route owned a
radar file. Broker-time observations were therefore lost if absent from the
last packet frame. `CycleRadarPool` now stores the latest normalized-name
binding from the existing profile reader throughout broker/prices. Range3000,
types1/3/8 and the packaged city collection polygon filter the pool.
No additional game reader/process is started during broker.

The pool resets at the beginning of the next broker observation. When the
broker finishes, its known names leave the pool and use the server queue.
Remaining names seed the price worker's initial atomic radar frame. Original
packet `observed_at` is preserved; writing the frame is not native validation.
An empty server assignment can still launch a radar-only price operation.

Packet coordinates nominate a single approach. The same ordinary shop reader
requires a matching native actor/class/generation and <=95 before each action.
It cancels the server target after the pair. Nearby fresh native absence can
skip this pass without fake checked/deletion/deferred. Reviewed radar names
are suppressed until the next broker pass, preventing an empty-route loop.
The operation600s limit remains; unattempted pool entries can continue in the
next operation, with no center return between batches.

## UI

`This pass`: new found, radar in pool, shops read. Local read means an exact
capture durably enqueued; server counters still represent acknowledged market
state. `New radar shops` lists up to150 pending names. Updates use the existing
one-second UI refresh; spool polling reduced2s→.5s. Counters survive server
state refresh and route batch boundaries; reset on next broker/start, not on
every worker process. Known broker names are removed from the new-shop list.

## Verification / deployment

- `ModuleIsolation.Smoke`: packet arrival during blocked fake broker, latest
  ObjectID, original time, disappearance from final frame, broker-known filter,
  empty server assignment still starts a route, reviewed absence does not
  count as read, no requeue, reset, radius/zone. No game access.
-88 geometry tests, including real route runner with fake client/reader for
  radar-only startup/hand-off, passed.
- `build.ps1`:796 verified files. Render inspected at
  `workspace/v46-cycle-ui.png`, no clipping of pass counters/tabs.
- V46 deployed14:27:796 verified,20 installed/restored. Owner12524,
  game10840 (rediscover), auto launch/collect requested. Runtime evidence still
  pending; builds do not prove actual broker-time handoff or repeated cycles.

V45 preceding route `bfe8ff667e9f4a81ae623acf4ea07ba8` completed14:26:19:
27 current exact shops131rows (19sell/4buy/4package),54requests27cancels,
0timeouts/invalid/errors. Four native-unavailable shops: starenie,T0RG4,
ko3akxcipko,wuk. Center was underway before normal V46 shutdown.
Earlier V45 `3ec58beb...` accepted2/2 current16rows and center `3ce5c44a...`
completed14:19:35 before that broker/price route. No purchases/second reader.

## Next live checks

Observe a full broker and its sibling `.radar-pool.json` after handoff; original
packet timestamps should lie during that broker. Follow those names through
radar admission/approach, exact capture and current server snapshot. Confirm
pool/counters reset at the next broker, full exhaustion/center/immediate
broker/next route and repeated passes. Diagnose unread attempts by native
absence, movement result and wire stats, not by a Deferred total.

## First V46 live handoff (14:34)

Broker `a3995a8f1f734481b24b0e634a73d305` ran14:28:34–14:30:22.
Its sibling `.radar-pool.json` retained4names not in final broker rows:
xujesos,IronBOX,DedGirana,LooTTrader. DedGirana packet timestamp14:30:07
and LooTTrader14:30:12 both fall during this broker. The other two observations
predate the broker and still nominate approaches with original packet times.

Price route `d8d299548aa64e649a8835afd1e714c1` started with20server targets;
all4pooled names appear in its `radar_new` log14:30:43. LooTTrader native
admitted at864.6units, exact14:30:51.873/current server14:30:52.430.
DedGirana exact14:31:46.806/current server14:31:47.242. Both fully active
current snapshots, not historical-only. LooTTrader read→next move0.0842s.
Additional radar discoveries continue during movement. At14:34:23exact23,
4assigned unread. Minisale was live on admission then absent29fresh native
scans at the read point:0requests/0timeouts, current-pass unavailable.
Full exhaustion/return/next broker/repeated cycles still pending.

Extra isolated coordinator assertions prove PassRead1→2 across separate route
batches/server refreshes and2→0 at the immediate next broker. Log:
`workspace/v46-module-tests.log`.
