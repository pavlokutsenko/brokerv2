# Delayed exit through actual Launcher

User reported a kick after a couple of minutes and explicitly required the
Launcher UI rather than the earlier short shared-launch harness. Earlier live
acceptance held each world for only seconds; it cannot establish long-term
stability. Do not describe those short runs as proof against this delayed exit.

2026-09-27 12:45 UTC: launched the actual portable
`release/PriceCheckLauncher/PriceCheck.Launcher.exe`, PID 12196, build
d013b2d8cda841608edae1fd680d25a5, with its existing --launch-profiles option.
Selected first saved Launcher Gamma/profile
b1cec477-8755-4bbe-8990-49c905bf4d5f and New template1, proxy off.
Production MainWindow, timer, ownership, template, protection and recovery run
unchanged. The test-only LauncherSoak.LiveProbe startup hook records journal
events and runtime/OS health every two seconds. It uses the authorized saved
Collector account in memory for manual native login; credentials are never
copied into Launcher settings or logs. No hook is included in distributions.

Game PID 432 reached the native character/world gate around 12:46 UTC.
HWID tag EF76B9EF4E81 belongs to this Launcher's own saved profile/template.
ProxyRequired=false, CONNECT=0, BlockedConnections=0, Health.Connected=true;
world incoming data rose beyond 860 KB in the first 31 seconds. At 332 seconds
the same PID was alive/responsive/connected, HWID/world remained confirmed,
blocked connections were zero, and received data exceeded 1.19 MB. No
protection error, server disconnection or timer-driven restart was recorded.
Brief unresponsiveness occurred during initial world loading and recovered;
do not classify that initial loading interval as the reported delayed exit.

Evidence: `workspace/launcher-soak/launcher-01.log`; native transport/HWID
logs under `%LOCALAPPDATA%/PriceCheckCollector/logs/*-432.*`.
The computer-use node kernel failed initialization and reset/retry with
"failed to write kernel assets ... path specified (os error 3)". No substitute
PowerShell UI automation was used. Actual app state is recorded in process.

That first run was historical: the user later launched the archive and supplied
a screenshot of a third profile. Its eventual exit was not attributed to a crash.
No test deadline terminates or rotates a client.

## Exact archive/profile reproduction and fix

13:01 UTC: restarted the user's actual extracted 151939 archive EXE after
checking no game was active. Runtime/native hashes matched the previous ZIP.
Third Gamma a59da7a3-294f-43d9-a323-13c34937138e, template
5122c397-a1af-47c8-bdf8-3407aa7f3cad / New template test, fixed HWID,
external proxy enabled. Rotation and recovery were off. Used normal saved
Launcher AutoLogin, not manual login or the short harness. The old game PID
17356 stopped at 13:02:39 with native login code -30. Busy UI showed stale
protection because the modal error preceded its finally update.

Diagnostic runs 02/03 and 04 captured fresh controller and agent heartbeat,
required=3, controller=1, flags=1, native error=0 immediately before exit.
No native failure or actual relay tunnel_error preceded closure. Run 04's
held process handle recorded exit code -1; retained protection was
"The operation has timed out." Only the separate periodic upstream probe
could produce this plain TimeoutException; transport errors have their own
messages. Background dispatch of login did not fix it and was not retained.
Evidence: workspace/launcher-soak/archive-proxy-01.log, -02.log, -03.log,
archive-proxy-04-background.log; proxy-tcp-8900.csv has healthy CONNECTs,
then cancellation rather than a tunnel error.

Removed the 10-second idle upstream CONNECT from ClientLaunchGuard. Preflight
before launch, authenticated CONNECT on each real socket, WFP route/PID checks,
native/API/world and heartbeat checks continue. TCP pump faults remain fatal.
Both UIs now refresh retained protection before a modal failure. Native lease
denials/failures have failure-only launch-guard-PID.txt diagnostics, without
credentials/HWID values. Reasons: 1 missing guard name, 2 mapping open, 3 view,
4 header/PID, 5 process-time API, 6 creation mismatch, 7 lease, 8 hardware flag;
20 + native error denotes the watchdog's first recorded failure.

BuildId 4ececef595fc4fb5b7b3f6a0df3625a4 / no-idle-connect passed build.ps1,
LaunchProtection.Smoke --wfp, native gates/watchdogs and both ZIP/apphost tests.
New regression refuses all unrelated CONNECTs while an existing protected
world remains alive for 11 seconds: request count must not rise. Refusing an
actual game CONNECT still fails protection and stops the test client.
ZIPs 162013/162019 replace old ZIP/checksum pairs; user extraction is preserved.

The screenshot account's next normal AutoLogin reached no character list and
stopped with -24; do not claim a successful world entry for that account.
The user accepted comparison with the previously verified saved Collector
account. The test observer passes that account only as the login service's
in-memory argument; it never edits the actual Launcher profile or its saved
credentials. Template, HWID, proxy, production UI, timer and guards stay the
same. No observer/experimental login wrapper is packaged.

13:19 UTC: real release Launcher PID 14632, game PID 16300 entered world using
that saved account and the third profile's template. HWID tag 65FC6E59AEDF,
native flags=3, world count=1, ProxyRequired/ProxyReady/HardwareReady/world=true.
At 217 seconds the PID was responsive/connected, CONNECT=7, blocked=0, received
1,816,311 bytes, error=null. At 317 seconds (over five minutes) the same game
was still responsive/connected with no protection error. This covers the
reported couple-minute delay for the verified account on this exact template;
it does not establish success for the screenshot account or every future session.
Evidence: fixed-proxy-saved-account.log and native/transport logs *-16300.*.
The game remains open; no automatic deadline closes it. Preserve unrelated
template-editor changes when committing this fix.
