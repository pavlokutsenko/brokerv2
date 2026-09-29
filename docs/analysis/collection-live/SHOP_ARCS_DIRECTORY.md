# Shop arcs and broker-only directory — 2026-09-27

User reports mixed arc/straight approaches and expects broker-only shops to be
listed before prices are read. Two independent causes are confirmed:

* `cycle_plan.price_route` used straight standoff passes, while radar detours
  and revisits already used `radar_pass_route` short forward arcs.
* The Market trader directory selected only `read_live_shops`, excluding active
  broker-only presence. Broker delivery itself is not proof of directory display.

Primary approaches now use the existing radius72,45-degree forward arc, with
the same55 planning exclusion and45 execution exclusion. Blocked arcs retain
the safe side pass or defer; obstacle/native capsule checks stay intact.
Approach mode and arc points are recorded in the plan. Near-anchor lookahead
is35 instead of95: the old95 could jump across the entire56-unit arc. Gate
limits remain applicable; neighboring price reads still share the route.

Offline checks cover every open-field anchor's13 arc points, all exclusions,
safe fallback when both tangent entries are obstructed, and the bounded follower.
38 geometry/follower checks plus the new fallback fixture pass. Root build
passes, BuildId c495e4af96fe468284c5363786990bee. Live movement remains to verify.

Market directory now includes active local presence-only shops with null price
timestamps and `Prices not read yet`. Count/search/detail share one source;
confirmed close hides the trader; exact price replaces the entry without a
duplicate. No price/profit projection is altered. All50 dedicated PostgreSQL
integration checks and predeploy verification pass. See canonical Market repo
`docs/trader-directory-presence.md`.

The16:05:37 full native radar had1722 supported shops,1721 inside the approved
polygon; Topr12 was outside. Center UI currently counts all visible supported
shops without polygon filtering, so a small residual count difference is valid.

## Regressions caught by live acceptance

The first published directory total2337 was not accepted: server-only legacy
active shops with no price were invisible to the collector's known-key history
requests. A1dka had a last observation on September22 and no broker/read time.
Complete active roster import now runs at center before the broker epoch. Only
unknown supported identities with approved positions are imported; this does
not invent current presence, a price or closure. Fresh independent radar still
decides retirement and protects newer server observations. Market commit
c9bcc9b adds an explicit completeness flag and a10000-row bound.

The user also observed jerky movement with lookahead35. The initial six measured
arcs did not establish smoothness. Lookahead is now72 near traders so commands
do not continually target almost-reached points. Main arcs, obstacle exclusion
and fallback guards remain. Build24f9328760f946db8c4a6682fc9cd71f passes root
build.ps1.39 geometry/follower checks and LocalCycle.Smoke including the unknown
server-roster fixture pass; Market predeploy and8 local-cycle HTTP/PostgreSQL
integration checks pass. Actual total/list reconciliation and smooth movement
remain pending until production deployment and a new center pass complete.

Collector25388/game24376 were closed by the agent for these fixes; this is an
authorized update, not a manual user stop. Preserve the profile/rotation.

The17:05:38 live native proof failed (2038 accepted identities) because two
guarded CharacterPlayer_C actors, Leprechaun/ObjectID1233157420 and JasonTod/
1291889961, have kiosk type6. Their identity/root/name/position were valid;
type6 is now retained in identity observations, while price eligibility remains
1/3/8. Unknown7 is still rejected. A final radar capture retries at most3 times
and keeps one independently complete snapshot, never a union of partial data.
Collector now holds in the broker phase on incomplete center evidence; after
three failures it stops rather than beginning prices without reconciliation.
Normal incomplete broker rows may still continue once independent radar is full.
14 focused worker tests pass; root build passes, Build550a123e65f740f7b8fc779cd24beab9.
No native offset, payload or route protection changed. Collector24472/game24776
were closed by the agent to install this correction. New Collector25592/game8720
connected its reader and began collection17:10:09 UTC. Current acceptance pending.

Native UI screenshot capture timed out in both runs (including normal visible
launch); do not claim visual smoothness from this unavailable channel. Movement
must be checked using actual native position/command samples and exact read ACKs.

The17:12:44 complete capture2057 identities/1729 supported shops (1728 in zone)
retired650. A1dka/HoleyMole/Lecsi were verified inactive by read-only history.
Entire-roster audit found25 missing native names and12 extras, unlike the earlier
single-card check. Several missing names were still server-inactive despite newer
local observations: ServerClosedHandledAt tracked only the unchanged server
closure, preventing a new positive observation from retrying reopening. The
store now also tracks the live observation used for that attempt. Repeated same
observation is bounded; a newer real observation retries. HTTP smoke covers both.
Torgashik227 was locally ConfirmedClosed but server-active sinceSeptember22;
the complete active roster now permits a fresh center absence proof for such a
name. Roster smoke covers repeated complete proof despite local close marker.

Market3a2f184 accepts known nonmarket state types (live MS23/type2 retry), keeps
price/broker admission1/3/8, and excludes current nonmarket types from the trader
directory even with historical inventory. API725 unit tests,9 PostgreSQL/HTTP
checks, typecheck/build pass; production deployment pending. No price projection
or history deletion is added. Build387aae0a4f1241e2910c97872cda1e64 passes root
build and extended LocalCycle smoke. Collector25592/game8720 closed by agent for
this fix, then Collector24568/game22372 began17:23:13 UTC. Temporary stage removed.

Motion acceptance remains bounded: native sample audit under72 recorded1111
commands, median goal distance72, zero goals under12 (old35 median35, two under12).
Actual route recoveries still include obstacles/stairs, four stalls and seven
unsafe-shortcut replans in this observed window. These are not proof of fully
smooth movement. Continue monitoring actual traces/read acknowledgements.

Whole-roster acceptance17:35 UTC (Build33bb05) found the final13 omitted
positive identities came from using complete native evidence only for absence
cleanup. The complete independent snapshot now also publishes every supported
positive1/3/8 identity through Observe(publishNewPresence:true), with its actual
observation timestamp/position and no invented price. Server active trading
roster membership bounds the missing-presence retry. Extended LocalCycle smoke
and root build passed. The fresh complete capture2055 identities/1759 supported
shops/1758 in the approved polygon had zero missing server identities.19 absent
shops were retired. A1dka/HoleyMole/Lecsi/Torgashik227/MS23 are server-inactive.
Later extras inside the polygon all have fresh observations after this capture.

Serverc1b795e deployed successfully (CI36337259605). The Giran directory excludes
known positions outside the collection bounds, preserving their history/state
instead of fabricating closure for unreachable old ZLL.725 API unit tests and10
PostgreSQL/HTTP checks passed along with typecheck/build and the full deployment
pipeline. Browser whole-list1771 (later1778) replaces the erroneous2337 total;
Torgashik227 search returns zero. Counts change with subsequent real openings.
At17:40,50 exact snapshots/253 rows were durable,48 acknowledged as current and2
retained historically by the server; SQLite outbox was empty. Historical receipt
does not count as current-price acceptance.

The same live motion exposed the separate jerky-routing regression:19 of22
radar detours targeted an anchor already scheduled later in the planned route.
The 500-unit corridor check could not see that future passing arc and repeatedly
paused/reversed the pawn. Coverage anchor keys now prevent those redundant
interruptions. Truly unplanned radar targets remain eligible; missed/stale
anchors retain the bounded revisit path.60 navigation/geometry/revisit checks
and build.ps1 pass. Build82ee39df23bf4bd889d053bc8d7df504 is installed. Collector
25420/game16812 were gracefully closed by the agent for this demonstrated fix,
not by the user. Collector20776/game22892 launched17:44:55/17:44:58 UTC with the
same profile/character rotation. Own temporary build stage removed. Fresh center,
whole-list and actual motion/price acceptance for this last build remain pending.

Build82ee39df live acceptance17:48:06: complete2054 identities/1765 supported
shops/1764 inside polygon, observer(82848.84,148363.27) within200.20 absences
retired. Full roster again has zero missing; three extras inside were observed
after the snapshot, plus outside historicalZLL hidden from Giran directory.
Motion17:50 confirms zero redundant planned-anchor detours (two legitimate
unplanned targets), seven actual primary arcs with radii67.13–75.51.41 exact
reads/200rows,40current ACK and one historical receipt. Broker presence queue
still draining, with no attempt errors. Old named ghost shops remain inactive.

Actual17:49:47 goals exposed another follower defect: a folded/hairpin path
placed a future waypoint behind the pawn while the next segment led forward,
then stalled at(82711.75,149198.56). Lookahead now rejects destinations behind
the local forward tangent, reducing the distance or requesting a guarded replan.
Near final arrival remains valid; ordinary forward arcs unchanged.63 focused
navigation checks pass, including hairpin reversal/replan and forward-arc cases.
One initial test incorrectly expected no possible forward destination; corrected
it to assert a forward goal, and separately cover a blocked forward leg.
Next build/live acceptance pending. Known map collisions are still guarded and
must not be described as fully eliminated by this direction correction.

Final forward tangent uses the next actual path segment, not a12-unit averaged
direction across the turn.64 navigation/geometry/revisit tests pass. Root build
0444a532e3364486890df3182f28241f passes and is installed; temporary stage removed.
Collector20776/game22892 gracefully agent-closed for the proven backward-goal
regression. New Collector22892 started17:55:21 UTC: PID reuse from the old client
is real, so birth/type checks are mandatory. Fresh acceptance is still pending.
Previous broker outbox drained completely before this authorized update.
