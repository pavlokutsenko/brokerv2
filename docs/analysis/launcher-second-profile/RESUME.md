# Second-profile readiness and slow-PC launch budgets

2026-09-27. The user reported a second-profile agent timeout on another PC,
requested a local test, then supplied Telegram Desktop/logs and explicitly
requested increasing all timeouts.

## Remote evidence

C:/Users/Pavel/Downloads/Telegram Desktop/logs contains agent-12152.txt,
agent-2440.txt and agent-5052.txt. Each contains only 10 bytes: UTF-16LE ready.
There is no failed-launch PID or initialization error in this evidence.
Neither insufficient time nor a missed Windows event is proven remotely.
Another-PC acceptance remains pending; do not claim its error reproduced here.

## Implementation

Agent completion is an explicit bit 4 in the existing guarded per-launch MMF,
written only after hooks and StartLaunchGuard succeed. Managed readiness checks
PID, process creation time, HWID bit 1, native error=0, controller_ready=1 and
fresh controller/agent heartbeat. A Windows event or stale text log cannot
authorize startup. Flags are 5 before world and 7 after world application;
required remains 1/3. The event is retained for legacy probes.

All desktop launch budgets are increased through PriceCheck.Windows.LaunchTimeouts
and native/ClientLaunch/launch_timeouts.h. Agent/pre-login: 60 seconds; game
process/window/hook and driver load: 300; full login: 180; native server/character
waits: 60/90; current world confirmation: 120; network/pipe: 30; DoH: 45;
heartbeat grace: 15; stop/drain: 30/90; recovery disconnect/stuck/missing world:
60/180/300. Driver readiness: 15, driver stop: 30, accept-loop cleanup: 6,
Collector refresh shutdown: 300. Poll intervals, user rotation and retry backoff
are unchanged. Reported protection failures remain immediate and gates stay closed
until verified. Game-provided select deadlines are preserved.

## Verification

build.ps1 compiled both self-contained products. Final BuildId:
ee10d4e57c454243ba84e991d1f3561e, label slow-pc-launch-timeouts.
The final compilation reached publication while our test Launcher was open;
publish-durable correctly refused overwriting it. The test instance was closed
normally, then both already compiled stages were published and packaged.
Driver loader keeps its UTF-8 BOM for Windows PowerShell 5.1.

Native gates/watchdogs pass, including longer heartbeat grace, stale controller,
wrong process birth, failed HWID/layout/envelope and explicit controller revoke.
LaunchProtection.Smoke --wfp passes, including the real managed ready gate:
a fake signaled event is denied, 16-second initialization succeeds without an event,
wrong birth/HWID/stale heartbeat are denied. Actual CONNECT failure and route/HWID
loss still stop fixture clients. Recovery-only smoke passes with simulated times
around the increased disconnect/stuck/world boundaries. The full ModuleIsolation
suite hit the concurrently changed one-reader-per-market invariant before reaching
recovery tests; its unrelated collection fixture was not changed to bypass that rule.

Two actual saved Launcher profiles were launched without account substitutions:
b1cec477-8755-4bbe-8990-49c905bf4d5f and
e84911d8-0abd-42c0-940a-874e15b085f3, New template1/2, fixed HWID, proxy off,
AutoLogin off. Build 694866... reached ready for PID 24568 at 18:02:01
and PID 22380 at 18:02:26. A later first-profile root-route error referenced
PID 5700; second profile stayed ready. A repeat (Launcher 23436) reached ready
for PID 23888 at 18:05:56 and PID 25556 at 18:06:22, with no such error before
normal test shutdown. Do not infer a world/login acceptance from these tests.

Concurrent work added process-birth ownership to the guard, addressing reused
root PIDs without ignoring a current game route loss. Its targeted --root-reuse
fixture passes; final binaries include it. Those source changes remain owned by
the other ongoing chat and are not staged as this task's changes.

Final archives:
- PriceCheckLauncher-win-x64-20260927-180831.zip, 75,048,290 bytes, 479 manifest files.
- PriceCheckCollector-win-x64-20260927-180838.zip, 102,026,552 bytes, 731 manifest files.

Previous ZIP/checksum pairs were removed only after verifying the new packages.
User-created extracted directories are preserved. Root is one EXE plus runtime.
No credentials, settings or logs are distributed. Final packaging and live
readiness evidence are recorded below.

Evidence: workspace/launcher-soak/slow-pc-{native-final,wfp,recovery,root-reuse}.log,
slow-pc-two-profiles.log, slow-pc-two-profiles-repeat.log,
slow-pc-final-two-profiles.log and slow-pc-final-package-smoke.log.

Final acceptance: Launcher PID 924, same BuildId ee10d4e..., reached ready
for first PID 25088 at 18:10:34 and second PID 21292 at 18:11:02. Second
lease flags=5, required=1, controller=1, error=0 with fresh heartbeat; no
agent timeout. This checks startup/HWID readiness, not entry into the world.
Final DesktopPackages.Smoke/PortableHost.Smoke pass on both ZIPs:
SHA256, single root EXE/runtime layout, PS5.1 verifier, same-length corruption
rejection, real WPF apphost with an independent cwd and no global .NET, and
unchanged user settings. Extraction:
workspace/desktop-packages-smoke-4436adabf41f4054bbcf6832ccb45399.
Both final apphosts are left available for the user. No credentials or settings
were modified. Do not close unrelated Collector processes owned by another chat.
