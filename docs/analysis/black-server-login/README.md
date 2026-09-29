# Black server login diagnosis — 2026-09-28

Goal: validate a Black profile's guarded autologin and concurrent Black/Gamma
market collection. No account, launch-template, proxy, HWID, ProcessEvent
guard, or game-junction setting was changed.

The Collector build `352b9e35b5e34fa5a8775bb44b01f249` was installed
after the prior UI owner and its owned Gamma client stopped cleanly. Gamma
launched and collected; Black was launched with its pre-existing account and
template. The native shared mapping for the Black client contained
`server_id=2`, slot 0, and reached status 3 (server call issued, awaiting
character roster). The client displayed "You have been disconnected from the
server" on the server selection screen. No character-list callback occurred,
and autologin timed out. The Black launch was repeated once while Gamma was
working and once after Gamma had stopped; the same failure occurred solo.
The solo result rules out a second-client limit as this failure's cause.

Gamma's existing ID 1 path remains proven. Black's ID 2 was inferred from
visible server ordering; the game highlight was already present from prior
manual use and therefore does not prove the protocol mapping. The selected
`LU4LoginMode.SelectGameServer` function at GObjects index 19227 accepts a
4-byte integer; current code passes `profile.LoginServerId` from the
PID-scoped mapping at offset 12 through the guarded game-thread ProcessEvent
slot 77. The native function reports invocation, not game-server acceptance.
The user manually reached the Black world, proving the account and route can
work. Returning to the server screen exposed an additional in-game selection
dialog after the four banner buttons. Choosing Black there and confirming it
reached the Black character roster. The automatic sequence passes `2` to
`LU4LoginMode.SelectGameServer` without traversing that dialog and instead
disconnects. A read-only MinHook observation of ProcessEvent was installed in
the user's test client before the manual selection. No call to the exact
`SelectGameServer` UFunction occurred during the manual path up to the
character roster; the dialog may use a different native/UI function. The
monitor did not alter calls or protection guards.

Read-only DBK inspection of this client recovered `GameServerInfo` fields
(`ServerID` offset 0, `Name` offset 0x28) and `Wid_Login_Servers_Item_C`'s
embedded `Game Server Info` at widget offset 0x318. At the character roster,
the live server item widgets had been destroyed; only the class default
object remained. A bounded read while each list was visible captured two
agreeing widget records per server: `Black=10`, `White=20`, and `Carmine=30`.
`Gamma=1` is already proven by the earlier production autologin. Black's
assumed `2` was wrong.
The raw, local-only captures are `workspace/server-list-report-2.json`,
`workspace/server-list-white.json`, and
`workspace/server-list-carmine-verified.json`. No user credentials were read
by these probes. The profile migration and creation dialog now use the
verified IDs; the remaining acceptance is an automatic Black world entry and
Black market ACK with Gamma running concurrently.

The release built with these IDs reached Black's real two-character roster on
28 September (owned game PID 24000), but the native character stage waited
until its 90-second deadline and the Collector stopped that owned client.
A second bounded run (owned PID 24140) recorded the reason without changing
the guard: the roster was already visible, while the launch guard still had
`flags=5`, `world_count=0`, `world_opened_tick=0`, and `applied_tick=0`.
`LaunchGuardAllowsLogin(true)` therefore denied the programmatic character
call. The misleading "character screen did not appear" error now has a
separate native status `-31` and explicitly reports missing guarded world
exchange. Retry duration and protection decisions are unchanged. No Black
market collection or simultaneous Black/Gamma run has been accepted yet.

After the user authorized guard changes, the character call was allowed with
the existing fresh HWID/lease checks; `LaunchModule` still requires the
verified current-world identity and bidirectional traffic before it reports
a successful launch or starts collection. That run entered Black's world
(owned PID 18736), but the post-entry check correctly stopped it because
both the native send hook and the Collector proxy broker recognized only
Gamma's world port 7782. An opt-in metadata-only proxy trace in a fresh Black
client (owned PID 2700) recorded login port 2108, an unrelated 443 tunnel,
then a successful and active world connection on port 9971. The trace did
not capture payloads or credentials. Both world-port classifiers now also
recognize 9971; the world identity rewrite and final guarded check remain
mandatory. White/Carmine world ports have not been observed and must not be
invented from their server IDs.

After the 28 September client update, Black again entered its world with the
guarded proxy identity applied, but the price route stopped at its old PE
timestamp check. Read-only discovery on owned Black PID 16488 found one
GObjects at RVA 0x81EA860, one FNamePool at 0x8133B00, and a structurally
valid GWorld at 0x836AA00. All route UFunctions were recovered by reflected
name, owner, class and parameter size; the updated ControllerPC_C movement
indices are 251100/251102/251113 and the LU4GameHUD shop callbacks are
19572/19574/19576/19578. The old numeric indices resolved unrelated objects.

The working route now performs bounded read-only discovery once for a new
image, caches only module-relative RVAs and object indices under LocalAppData,
and validates the tables, world chain and every reflected function against
each live PID before reuse. A cold resolution took 8.6 seconds; validated
cache reuse took 0.2 seconds. The target sender's DirectHook and post-send
sites use unique full code signatures. The receive observer reuses its
existing unique signature resolver. The MoveToObject suppression hook resolves
its handler by code prefix and its one writable table reference, retaining
the message slot, neighboring function-pointer and ownership guards. On this
client the sites resolve to DirectHook RVA 0x12C84A0, post-send 0x4D0C150,
receive 0x4D0971C, and MoveToObject handler/table slot 0x4CFCEC0/0x803D690.
These values are evidence only, not runtime constants. Broker target-order
FName is now decoded as `TargetSelected_Order` (live ID 71918), and broker
actor binding uses the same validated runtime profile as the route.

Read-only acceptance on the live Black PID: resolved world class `World`,
controller and trader class, 126 actor bindings with no rejected actors,
and independently complete actor snapshots. The 43 market walk/capsule/
preparation tests pass, and `build.ps1` produced the updated Collector. Live
movement, price ACK, Black market upload and simultaneous Gamma collection
remain to be verified after installing that build.

After installation, live Black PID 18792 completed native movement and
individual price reads with current server ACKs. A manual disconnect briefly
left the world chain on a non-player capsule; the existing class guard blocked
commands until the owned client returned to a valid `CapsuleComponent`.
Black market history subsequently returned 85 traders with prices and the
local Black `latest_broker_delivery` recorded 103 accepted traders, outbox 0.

With five-minute rotation enabled on both profiles, Gamma and Black entered
world concurrently and collected concurrently. In the 11:57 UTC minute,
Gamma issued 53 guarded move commands and Black 64; both produced exact
reads and current individual server ACKs. Their broker intervals overlapped
(Gamma 11:54:55–11:56:50, Black 11:55:12–11:56:45 UTC). Black's independent
center radar was complete at 11:56:45 with 133 identities and ten guarded
retirements. One broker row for ObjectID 1210069449 had no actor binding in
either packet or native actor snapshots; the incomplete quantity row was
retained without inventing a trader identity, while the 103 named traders
were uploaded and price movement continued.

The previous Black pass had an incomplete center radar after the player
drifted outside radius 200 during the long broker request. The retry phase
incorrectly called broker again from outside center and stopped collection.
`CollectionModule.Cycle` now routes incomplete-radar retries through the
guarded return-to-center phase and checks the current radar before every
broker start. Build `center-retry-collector` and LocalCycle smoke passed;
the installed copy was hash-verified before a new dual-client run. This
removes the terminal outside-center retry, but a naturally incomplete radar
on the new build is still needed to witness that exact retry path live.
