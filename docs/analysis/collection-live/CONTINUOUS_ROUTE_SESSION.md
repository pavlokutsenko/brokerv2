# Continuous price session — 2026-09-27

Pavel observed a long pause when the next section loads and another route-build
failure. Prefetch821ad did use a real cached path (0.791s), but the host still
ended its native process, settled movement and removed/reinstalled the shop
reader between every section. That lifecycle gap remained after faster geometry.

The host now executes the finite price pass in one asynchronous loop. Unique,
atomic commands submit successive sections to one `market-route-session` worker.
One WalkClient owns its PID and one WalkShops keeps its hooks, reader thread,
pending replies and event sequence throughout the pass. Per-section target
membership changes under the reader lock. A completed coverage path hands off
ordinary movement without final stop sampling. Real blockage/replan pauses and
bounded exact-response waits remain guarded. Native final rest and hook cleanup
occur at final pass completion, cancellation, failure or client rotation; the
host awaits cleanup before changing role. Replies drained during final cleanup
are persisted before the shared event tail is released.

The separate `market-plan` process continues to refresh its next-section plan
when the local queue changes during movement. It performs no native calls.
The executor checks map/capsule/targets/generation hashes and connects from the
actual position. A changed queue rejects the cached path; validated immutable
navigation geometry remains reusable even across sections of the same session.
Commands cannot reference another runs directory, change profile/market/city,
or replay a section. An idle command lane expires after10s if its host disappears.

Native failure independently reproduced at18:56:04UTC in the still-owned
Gamma15996 birth18:35:45.2906846Z. Source82808.453786/149390.786900/-3478
was in shallow initial contact with CharacterNpc_C / CharacterCapsule,
CapsuleComponent, penetration2.193010, time0, normal(-.686989,-.726668,0).
Every departure sweep began penetrating, so the old<=2 wall-contact guard
rejected departure until20s timeout. Only this exact NPC capsule case now
permits penetration<=3; wall limit<=2 remains unchanged. The short move must
face outward and a native followup sweep beyond the initial contact must be
clear. No position writes, new offsets, native DLL or protection changes.

`initial_overlap_probe.py` freshly confirmed that same NPC contact, clear outward
followup and ordinary65-unit goal82763.799511/149343.553483. Actual position
82773.896550/149354.455863, observed displacement50.141269. Final ordinary stop
and cleanup completed. Private evidence: watch/initial-npc-departure.json.
Agent paused only its own route to conduct this probe; this was not user stop.

Verification:78 existing navigation/collision tests pass; four new session tests
execute successive sections through the real section runner and prove single
install/reader, retained pending reply, cancellation cleanup, fault acknowledgement
and replay/profile-directory rejection. LocalCycle smoke (generation/ACK,
durability, incremental tail, centre evidence, retention, room65/new priority)
passes. Root build and packaged source hashes must be checked before publication.

Live continuous-session timing, exact reads/current server ACK and actual
single-entry temple coverage remain to be verified after installing this build.
Do not treat source tests or an offline temple plan as live acceptance.

Installed build9a39261dc8c04359816e6c7cd1c42719, durable846 verified files,
66 changed; stage removed. Collector24012 birth19:06:46.2992642Z,
Gamma19768 birth19:06:50.3579880Z, collection19:07:19.1645505UTC.
Packaged five runtime-source hashes match source. Seven session/NPC tests run
against packaged modules with source Python pass. Frozen import of route_session
and run_section passes. Running the test itself in frozen executable is not
supported (`unittest.mock` intentionally absent); no runtime dependency added.
Actual centre arrival19:08:09, broker begins19:08:10. First live price section
and continuous boundary acceptance still pending.

First actual9a3926 session starts19:10:05, native worker20472. Second section
uses the same worker/reader (`reader_session reused=true`) and cached route+
geometry, prep0.478s;35 exact shops/192 rows/35 current ACK at19:11:56UTC.
However previous section acknowledgement to next move was still2.729s, last
command to next3.013s. This exposed another real gap: follow_with_recovery
unconditionally rebuilt Navigation for dynamic detours even after accepting
cached geometry, and revisits rebuilt the same city. The session's immutable
execution geometry now supplies isolated forks to both; learned blockers do
not leak into another section. WalkGuard is retained by that same owned client.
Two additional regressions verify isolated geometry reuse and completed coverage
handoff with no stop or sleep.84 source checks pass; next build/live timing
must confirm the removal of this remaining gap. No new offsets/DLL changes.

Complete centre19:10:01.433UTC, observer82747.234476/148291.747290 (inside200),
1817 supported zone shops allactive on server. Three newer opens explain three
extra active rows; legacy ZLL is outside collection polygon and intentionally
kept in historical storage, excluded from scoped web directory. HoleyMole,
Lecsi,A1dka,Torgashik227,MS23 inactive. Both requested corner traders were absent
in fresh native roster and inactive server; no pretend exactread for closed shop.

Further build388335a5166b4875a303c38971bb1679 installed19:14:50UTC after84
tests/rootbuild. Eight packaged runtime source hashes match; durable848 files,
63 changed, own stage removed. Collector8728 birth19:14:50.7726152Z; subsequent
owned game/birth/log/start come only from watch state. Prior24012/19768 stopped
by the agent for the newly proven2.729s geometry gap, not user cancellation.
9a3926 had45reads/238rows/45currentACK and one reader across three sections;
second/third prep0.478/0.485 but boundary gaps2.729/2.146 still needed the fix.
388335 requires fresh live acceptance, not those previous-run measurements.

388335 live proved that cached boundaries dropped to1.163–1.357s with the same
reader across eight sections,116 exact reads/554rows/114currentACK+2historical.
But a late RedEyes insert19:19:23 changed the next section19:19:26; cached route
could not cover it (352.09 units from staged path), so strict rejection was
correct and synchronous rebuilding still caused5.064s. Later invalidated-cache
boundaries3.91/7.506s confirm this is a remaining real loading pause.

The next revision preserves strict hash checks and starts a short native-guarded
passing approach to a reachable priority target while a single offline planner
prepares the full current section. The host sees its atomic preparation request
even before the main plan exists, serializes it with its existing next-section
planner, and subsequently resumes future prefetch. No additional native owner.
Prefix uses the existing verified reader/geometry and all movement/action guards.
The completed full plan reconnects from the observed prefix endpoint and skips
already captured leading arcs. Planner failure/no safe prefix retains bounded
fallback; cancellation never advances into the main body. At most5s bounded
cache wait after productive prefix, no unvalidated movement while waiting.
Per-section preparation inputs/outputs are removed after worker acknowledgement.
Persistent shop acknowledgements parse only complete newline-terminated UTF8
events, retaining incomplete appends for the shared durable stream.

87 tests now cover productive movement while planner output is completed,
skip-captured prefix, unsafe/too-close prefix refusal, cancel before cache wait,
and incomplete UTF8 append during real successive section execution. One initial
test used a hardware-specific100ms threshold (actual109ms); it now asserts no
sleep/cache waiting when the plan is already completed. Live next revision still
required. Do not claim the late-queue pause fixed from these source tests alone.

Installed9941b3dc8af44686978199f24cb86a96 at19:31:43UTC, Collector4524 birth
19:31:43.9588809Z. New Game19152/start/birth from watch state. Durable852 files,
66 changed, nine runtime-source hashes match,87 tests/rootbuild and12 tests
against packaged modules pass. Own stage removed.8728/9600 closed by agent for
the proven late-queue pause; no full pass claimed from that interrupted run.
9941 actual prefix/background overlap, current ACK, temple/fullpass pending.

9941 first live prefix Binansovna at19:35 proved actual guarded movement2.57s
and exact read/currentACK while the planner computed. Its full current plan was
accepted, but standing cache wait4.357s remained because the nearby first target
finished too quickly and offline preparation rebuilt the city despite available
geometry. This is measured further evidence, not a speculative restart.

The following revision reuses verified cached map geometry in offline `prepare`
even when target hash changes (always computes a new target-specific route).
While it calculates, the native owner chains up to five reachable unread
priority-target approaches, total25s bounded, checking for completed plan after
each. It does not deliberately stand after a short first approach while other
safe work exists. Final cache wait remains bounded when no safe work is available;
cancel during that wait returns before fallback/main movement. New tests prove
two productive approaches without sleep while output completes, all leading
captured anchors skipped, and no Navigation constructor in offline reprepare.
89 tests/rootbuild required; new live no-idle-wait acceptance pending.

Current live acceptance and further far-transit fix continue in BACKGROUND_MOVING_ACCEPTANCE.md; this file preserves previous design measurements.
