# Individual trader fly-bys in different market corners

2026-09-25. User accepted the current reading/cancel smoothness, asked to stop
the broad scan and test individual targets in different corners, pass without
approaching point-blank and continue immediately; exercise obstacles as well.

Previous full run 20260925-132324 completed before the new request: 356.545s,
286 shops / 1554 lots, 288 cancellations / 288 replies, selected pointer zero,
zero final drift. Three bounded recoveries (clearance, NPC, stalled waypoint),
return ~2.9 units from start. All hooks restored. STOP requested again; no Python
or BrokerWorker remained. Current task keeps the working action/action/cancel/
move order, explicit server range check, ordinary collision guards and cleanup.

New isolated mode: select a small set of live traders across market corners,
build continuous tangent/arc fly-bys with a stand-off exclusion circle, read
only selected trader keys, keep moving if a shop is unavailable. Geometry and
the original market maps remain preserved. Record actual closest approach,
read position, continuation after read, server cancellation and obstacle events.
## Initial control and corrected actor guard

Run `20260925-133745`: completed all four corners in 91.604s, no recovery,
262 native capsule checks, closest actual approach 53.64–55.85 units. However,
zero shop requests: all ten attempts were skipped by a new actor-class check.
Read-only inspection confirmed our pawn is `CharacterMain_C`, whereas all four
traders are `CharacterPlayer_C`. Comparing them to the pawn class was wrong.
The corrected guard resolves GObjects index **218705**, validates the name
`CharacterPlayer_C` and class `BlueprintGeneratedClass`, then compares the live
actor's class pointer. No name search or process scan in the working path.
Reused/streamed-out actors still skip. Added a regression for differing local/
remote classes and explicit rejection diagnostics. All 25 geometry/shop tests
pass; isolated `build.ps1 -SkipBroker` passed.

## Successful live pass

Repeat **20260925-134332 completed** in 91.254s: 10,709 units traveled,
227 movement commands, 262 guarded capsule queries, zero stalls/recoveries.
Four of four selected shops answered, **22 lots**, all `wire_int64` (normal
sell responses). Eight actions, four explicit server cancels, four server
cancellation replies; zero timeouts, range skips or invalid replies.

| Trader / corner | Lots | Distance at read, client / server | Closest actual approach |
|---|---:|---:|---:|
| SetkaPipetka / north-west | 2 | 67.29 / 68.24 | 55.78 |
| Tierra / north-east | 15 | 71.32 / 80.49 | 57.32 |
| twwin003 / south-east | 1 | 61.86 / 64.13 | 59.03 |
| theTrade / south-west | 4 | 71.10 / 72.09 | 55.83 |

The path continued after every read: samples 0.8–1.4s after the captured response
were 141–209 units from the read-request position. This includes response latency;
it is a continuation check, not a network latency or precise speed estimate.
Across 460 path samples: zero backward arc steps greater than three units,
mean sampled speed 123.21, stationary sample fraction 1.53%. The earlier
movement-only control was 122.84 / 2.74%. No visual claim beyond these measurements.

Returned 4.70 units from the start, stopped at
`(81293.995,147650.178,-3473)`, zero final drift, selected pointer zero.
Separate read-only verification confirmed original ProcessEvent/direct/post/
approach entries and the pre-existing RX observer restored; capture-state absent.
No Python or BrokerWorker remained. All 25 WorldGeometry, five sender and two
wire-decoder tests pass; isolated WPF build passed without replacing the release.

Saved actual path: `maps/giran-single-targets-2026-09-25.png` and companion JSON.
The original market/navigation/obstacle maps were preserved. The path passed
within 150 units of 17 mapped components; this tests this route, not every face
of every obstacle or unseen/new world geometry. Live NPC/capsule checks and
bounded recovery/skip remain active for future runs. This diagnostic writes local
JSONL and does not publish to broker-server.

Runtime files: `%LOCALAPPDATA%/PriceCheckCollector/research/market-walk/19308/`
with prefix `20260925-134332`: `.plan.json`, `.jsonl`, `.shops.jsonl`,
`.result.json`, `.verification.json`. Repeat with:

```powershell
& "C:\broker\test-market-walk.ps1" -SingleTargets
```

Stop with F8 or `& "C:\broker\test-market-walk.ps1" -Stop`.
