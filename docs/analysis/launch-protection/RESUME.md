# Live acceptance checkpoint

2026-09-27 11:15 UTC: the user requested removing extra checks and adding them
one at a time. Baseline and all incremental stages passed world entry.
The full production path passed both slots and failure injection. The user
subsequently lifted the archive hold, then requested a single EXE at each
portable root. Current BuildId d013b2d8cda841608edae1fd680d25a5 uses the SDK
apphost plus runtime directory. ProxyEnabled=false now skips all external
proxy field validation and preflight/periodic probes. The owned local relay
connects directly to the original IPv4 endpoint; HWID, world generation,
PID/birth and controller checks remain active. UDP/raw/IPv6 remain denied.
Native required flags allow only 1 (HWID) or 3 (HWID+proxy). Agent/login were
rebuilt; driver/BrokerWorker are unchanged. Both new ZIPs passed
DesktopPackages.Smoke and actual apphost/WPF loading with a test-only startup
hook; profile settings are unchanged. Two complete live runs passed this code:
proxy off and proxy on, slots 0/1 with Collector reader then Launcher rotation,
continuous checks and owned relay revocation. Expected HWID tag E333C54BA1BC
matches the selected saved template. Logs:
`workspace/launch-protection-live/optional-proxy-direct.log` (PID 20848/15692,
CONNECT=0, ProxyRequired=false) and `optional-proxy-enabled.log` (CONNECT=6).
The direct-mode echo test waits 11 seconds with invalid unused proxy fields,
then injects HWID failure and confirms termination. Old ZIP/checksum pairs
are pruned only after a verified replacement; user-extracted folders are retained.
See
[DESKTOP_PACKAGES.md](../../DESKTOP_PACKAGES.md) for names and checksums.

Confirmed baseline: ordinary root launch, final PID bound to the same current
driver and proxy, existing HWID agent with no new guard mapping, reader before
login. PID 12348 entered world at 10:54:42 UTC, stayed for 15 seconds, and the
native world-identity rewrite reported success. Both routes had CONNECT 200.
Raw log: `workspace/launch-protection-live/baseline-01.log`.

Attempt 11 also confirmed WFP reconstructs login 194.180.209.45:2108, after
the game's existing connect transform of its initial 185.29.255.102 address.
Both endpoints return 98-byte greetings independently. A benign test child
through the guarded broker/current WFP driver also receives 98 bytes. Thus
the generic proxy/relay is functioning. The user's UI observation was no
visible error, loading after login, then client closure/crash.

Verified and loaded driver SHA256:
`7DDB36E3C6201093F8A3471EC757E9BDA0C760DDCEE33D58AA0B75CF32703F11`, capabilities 31.
The tested bytes were promoted into the tracked artifact and pinned loader.
After the portable layout change, the service now points to
`release/PriceCheckCollector/runtime/DriverRuntime/lu4_memory_wfp.sys`.
Only its saved path changed; the identical loaded driver was not reloaded.

Incremental results after the user's request, each added independently:

1. Root suspension and inherited route before execution: PID 21724 passed.
2. One actual HWID API readback: PID 21872 passed.
3. Repeated HWID API readbacks every 500 ms: PID 22320 passed and remained alive.
4. Add repeated active64 layout reads: PID 18948 passed and remained alive.
5. Add native guard mapping, controller lease and send/login/character gates:
   PID 21720 passed; Hardware=true, World=true, application count=1, error=0.
6. Add separate upstream probes every 10 seconds: PID 21872 passed.

Increment 07 (`--managed-guard`), PID 5324 passed: manual heartbeat replaced
with the real managed coordinator and its admission/route/world checks.
The first harness attempt instantiated its guard before binding the suspended
root route and was correctly rejected before any game code ran; disposal
terminated the suspended root. Corrected the harness ordering and reran.
Raw logs are `workspace/launch-protection-live/increment-*.log`.

Native readback-only stages are separate compile-time probe artifacts under
`workspace/guard-probe-stage{1,2,3}`. No production runtime bypass was added;
with a guard mapping those artifacts still use the mandatory guarded path.
Restore the normal agent in the runner before the final production acceptance.

`attempt-12-standard.log` passed: PID 21764 slot 0 with reader before login,
PID 2692 slot 1 without reader before login. Both confirmed hardware hooks,
world identity rewrite and bidirectional proxy traffic. Route revocation at
11:08:52 UTC stopped slot 1 and the next character gate was rejected. Test
completed with LAUNCH_PROTECTION_LIVE_OK and unchanged settings hashes.

The exact cause of attempts 04-11 remains unproven. All checks passed in the
incremental sequence and the full combination then passed after explicitly
restoring the normal agent. Do not assert a specific check caused the closures.
The old WFP research also records transient anticheat behaviour after driver
loading; this is context, not a demonstrated explanation for today's failure.

Driver/UDP6 and kernel lifetime regressions passed. A test that expected socket
bind denial was corrected to target a forbidden endpoint distinct from the
allowed relay; normal TCP/UDP binds are intentionally permitted. Native probe
now distinguishes pending pre-login envelope from failed post-world envelope.
All native gates/watchdogs passed. `build.ps1 -SkipPackages` succeeded, BuildId
`efb2a79169f2491ea7a71e310b254b49` for both apps. No ZIP created.

Final: `attempt-13-final-build.log` passed at 11:16:21 UTC, fresh native DLL SHA256
`8C991C8881BE0137809F8ED0238490DA38E4B6E23930CC1A15B116831F5112A9`, verified
identical in the runner and both releases. Slots 0/1, world HWID, continuous
checks, route revocation and settings immutability passed again. Launcher UI,
Collector rotation UI and ModuleIsolation smoke passed. The initial UI test
invocation lacked its required output-directory argument; correcting the
command resolved that harness error without application changes.

All test-owned clients were stopped. No new archives created. Documentation
reflects both the verified results and unresolved cause of the early closures.
Keep account/proxy secrets out of tracked files and logs.
