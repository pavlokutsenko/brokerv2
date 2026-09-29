# Resume

2026-09-29: Release Collector PID 9020 and Gamma PID 3164 were running at the
last check, with `CollectionEnabled=false` and auto-login disabled. The user
was asked to enter Gamma manually so the world phase can be observed without
starting market collection. Rediscover PID, process name and CreationDate
before acting. Port metadata is under the current `proxy-tcp-*.csv` logs; do
not print addresses, credentials or raw packets. The current launch's
pre-world metadata contains DNS and one other launcher destination only.
At 16:09 Europe/Kiev the same Collector and game PIDs were still alive, the
Collector log had no newer world-entry event, and `CollectionEnabled` was
still false. Do not treat the lack of world traffic as an anticheat failure:
auto-login is disabled and the game awaits a manual login.

Prior evidence already isolates a direct early `clmods64.dll` `ComputerName`
registry query; no transmission or rejection cause is established. See
README and the linked registry stack observation. No app source or release
change was made in this run.

Later in this run the user authorized auto-login. Gamma's setting is now true;
Black's is unchanged and collection is still disabled for both. Release
Collector PID 12344 / Gamma PID 12352 entered the world successfully. The
destination-port trace showed launcher DNS/service, then game login and world
channels. A 125-second poll found no separate `AAErrorPort` TCP connection;
port 443 was Collector-owned and not in the game's redirect log.

A second bounded release diagnostic (Collector PID 17400 / Gamma PID 3120)
measured login/world relay byte totals and eight approximately periodic
27-byte world-stream reads without recording payload. The native hardware
trace exhausted its budget in 7.2 seconds because the published DLL still
logs a generic IOCTL event. See README for counts and limitations. The
diagnostic session was closed. Ordinary release Collector PID 300 / Gamma PID
1564 were then left in the world, with successful identity application, no
trace flags, auto-login on and market collection off. Rediscover all PIDs on
resume. No app source or release binary was changed in this run.

The 2026-09-29 read-only driver pass used the existing Ghidra project and
verified that its `active64.sys` matches the current running driver file by
MD5. Visible system-information calls enumerate kernel modules; visible
process-information calls inspect process state. Generic registry helper
imports yielded no concrete identity value. Details and limits are in
`observations/2026-09-29-driver-import-callers.md`. Collector PID 300 and
Gamma PID 1564 were still alive at 16:36 Europe/Kiev; collection was not
started. The raw local output is under `workspace/identity-import-callers.txt`.
The same driver contains three `CPUID` functions (nine instructions) for
vendor/features, Hyper-V capabilities and a timing path. No processor serial
or brand-string leaf was observed in those functions; execution and network
transmission remain unproven.

No CPUID hook was added: the relevant instructions execute in the third-party
kernel driver, while this repository's driver is not a hypervisor. On the
user's 2026-09-29 cleanup request, 5.12 GiB of ignored captures, duplicate
test builds and package-recovery directories were selected for deletion, but
the environment rejected the command before execution. They remain in
`workspace`. Do not report them as deleted. Preserve the current `release`,
LocalAppData market history and live processes.
