# Native absence must not stop the whole market

2026-09-26, V42. User screenshot: collector Stopped, Zizel ready on server,
Approach diagnosis required: ZIZEL, pawn82817,147621. This is an actual
whole-collector stop after3native-absent attempts, not a navigation calculation.

## Evidence

V41 second brokerdf736151 server complete=true1674/8262 at13:15:05.
Maina127b144 accepted11exact (7assigned+4new),54rows. Omamori inside temple
83968,148677 read at53.55units, wire_int64,4rows, after central entrance.
Next9e3b4c04 accepted2new,5rows, but no Zizel; cc6a088d and643c706d also
no Zizel. Last arrival has0requests/native actor absent near67units;
fresh origin .093s. Three native scans near81/75/96units all actor_present=false.
The server job remains ready. No authority to delete or mark a price checked.

Central exit9e3b4c04 at13:17:01: current83885,148615→83793,148625;
hit front_body point83848,148621, normal(.774,-.286,.565), penetration2.858.
Pawn continued to83853,148619 while settling. The sloped facade tread is a
false early capsule block for ordinary centerline crossing; bounded recovery
got outside. V41 folded-goal check also triggered once13:17:09 and replanned
immediately instead of repeatedly sending a3-unit command.

## V42 changes

- Reader counts consecutive fresh native scans without each allowed key.
  After a real completed approach/read window, only if distance≤100,
  last scan≤1s, absence≥3, no request sent, no captured/live actor/reader error,
  hold_read classifies temporarily_unavailable. It does not infer departure.
- Result temporarilyUnavailable skips that key for this broker pass. C#
  removes it from RemainingVisitKeys and approach diagnosis, releases the
  server lease READY with no failure/deadline. Other targets continue, then
  center→broker refreshes identity/position. Only full valid broker cleans up.
  Geometry errors and unanswered sent requests retain normal bounded retries.
- Ordinary gate probe additionally accepts the observed sloped facade tread
  only with upward normalZ.4.. .8, horizontal normal directed across facade
  ≥.9, impact≤35 from an arch center, goal≤10 from center, known facade and
  Xband. Existing length≤160,3.5s/5commands and actual progress guards remain.
  Side faces/off-center/pure horizontal ambiguous contacts remain blocked.

83geometry regressions/build.ps1 passed. Durable794files installed13:22;
owner10532/game16804 (rediscover), --collect-profile auto-start requested.
Previous10184/6204 closed normally, worker hooks removed, outbox drained.
No second reader, purchases, coordinate writes, DLL changes or server deadline
changes.

## V42 live confirmation and V43 endpoint correction

13:25 broker full1684/8350. c5cd9870+a6556e3d covered38/38assigned and9new,
47exact accepted244rows; center completed13:28:49, next broker immediately.
Zizel reappeared as a live actor and read in.104s/2requests; exact current price
accepted13:27:14. The prior native absence was not a decoder failure.

13:29 broker full1687/8359. e108954c+7895a683 covered12/12assigned+9new,
21exact accepted82rows. sukro was read in the second packet. Center completed
13:32:30, next broker full1691/8368 at13:33:05. b1cf2b6f+5a6d7179 stored8exact.

Partial broker13:35:47 did not clean up. Route1ed3eaf3 completed10exact39rows,
with MERHA and Akcija temporarilyUnavailable after nearby fresh native absence.
C# released READY without errors/retry deadline, continued to center13:37:56
and full broker13:38:31 (1682/8372). MERHA then inactive/CANCELLED only through
full broker. Akcija read13:38:55, current COMPLETED,0attempts. No market stop.
430e11c9+384aef81 stored9exact60rows before authorized V43 restart. Deferred0.

QmyTrade5 in c5cd9870 exposed another delay: game accepted endpoint at5.58units,
but individual arrival required arc error≤5 despite position tolerance8.
V43 aligns arc and position tolerances (8individual/23ordinary). Regression
holds a pawn5.58units from endpoint and proves immediate read handoff, avoiding
the old3s stalled recovery.84geometry tests/build passed,795verified files;
installed13:40, owner16740/game18400/uploader11596 (rediscover), enabled.
V43 real repeated-cycle verification is recorded in NEXT_SESSION.md.
