# Buy-shop capture on the September 2026 LU4 client

On 2026-09-28, Collector build `256d76b1f4ed4f50bfe7f1990d796935` ran
Gamma, Black, and White simultaneously. All three entered the world, completed
center radar and broker phases, and sent exact sell-shop reads with individual
server ACKs. Their buy shops (kiosk type 3) repeatedly failed the conservative
`shop side/count mismatch` check. Those replies were not published; outboxes
remained empty. The initial run used Collector PID 21760 and game PIDs 18324,
22244, and 22228, respectively.

The per-PID `process_event_shop_capture_state.json` files under LocalAppData
showed that the current reflected function IDs are `73295` for
`PlayerShopBuyItemsList` and `73318` for `PlayerShopNewBuyItemsList` on all
three clients. The hook's capture filter used the resolved IDs, while its
buy-versus-sell branch still compared against the old `71700` and `71723`.
Consequently it copied buy responses from the sell-array offsets and labeled
them sell. This explains why the error was confined to kiosk type 3 across
servers.

The branch now takes the same validated, per-PID function IDs used by the
capture filter. A focused test inspects the emitted comparisons using the
current IDs. The test passed and `build.ps1 -SkipPackages` published build
`295ae644599e4e8194818f6c140beee0`. The packaged script's SHA-256 matched
the source. Keep any unmatched or partial reply unverified; the row and wire
checks were not weakened.

The new Collector PID 11344 launched Gamma 21756, Black 14208, and White 16936
with collection enabled. All three entered the world, reached the saved center,
completed a broker phase, and began exact routes. At 18:39 Europe/Kiev, their
buy-shop exact reads and individual current server ACKs were Gamma 13/13,
Black 17/17, White 5/5. There were zero new side/count mismatches and zero
collector errors. By 18:40, all three local outboxes were empty after White's
subsequent broker phase. Total exact reads/current ACKs were Gamma 61/61,
Black 23/23, and White 5/5. Black retained one
broker row without a trader binding, as in the earlier run; the other 118
named traders were acknowledged. This does not affect the verified buy-shop
price reads, but the unbound row remains a separate limitation.

Extended live monitoring around 19:00 Europe/Kiev matched snapshot IDs in the
read-only market SQLite stores to individual server ACK events in the active
Collector log. For this build, all complete buy snapshots were accepted as
current: Gamma 88/88, Black 19/19, and White 5/5. There were no new invalid
exact replies or Collector errors. The few server-retained historical reads
were sell snapshots (three on Gamma and one on Black), not buy-side capture
failures. All three market outboxes were empty at that check.
