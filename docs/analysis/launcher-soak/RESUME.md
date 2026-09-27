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

The actual Launcher remains open with its observer active; no test deadline
terminates or rotates the client. The user's delayed kick has not been
reproduced on the first saved profile. A clarification about the first/second
Gamma profile was sent while this first profile was being observed.
Next: preserve the last state before any actual kick and distinguish native/
managed protection error, server transport closure, ordinary process exit or
Launcher recovery. Do not remove checks without evidence. Preserve unrelated
template-editor changes in the tree.
