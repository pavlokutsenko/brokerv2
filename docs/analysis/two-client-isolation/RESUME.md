# Resume: two clients in the same Gamma world

## Successful observed interval; monitoring stopped by user — 2026-09-24

The user accepted the working pair and requested stopping the monitor.
09:35:28–12:13:48 UTC: 951 samples over 2 h 38 min 20 s; no new world EOF.
914 samples fully healthy, 35 missing position checks during the repaired
profile-binding reset, 2 transient metadata backlogs; no missing sample intervals.
Graceful stop at 12:13:58 UTC; automation `gamma` is PAUSED. Do not restart it
automatically. Games remained open, with original identities/bindings and
established TCP rechecked at 12:14:46 UTC. Future actions must rediscover PIDs.
Private final summary: LocalAppData
`PriceCheckCollector/research/long-monitor/20260924-123527/final-summary.json`.
Success applies to this observed local configuration, not a full 24-hour run,
the new two-module build or an untested second physical PC.
See [monitor details and limits](observations/2026-09-24-long-monitor.md).

## Current checkpoint — 2026-09-24, 7-minute solo and 10-minute pair passed

Resumed with new accounts. Initial solo trials failed with the old identities;
proxy-only change failed too. Fresh fixed HWIDs passed entry. Original proxy
settings were then restored at the user's request. Black PID 11152 passed
420 seconds solo; Gamma 19660 and Black 14980 passed 602 seconds together
(116 valid samples). Collector 18620 and both clients remain open; rediscover
all PIDs. Periodic 27-byte traffic continued, with no late EOF in this interval.
Regenerate/RotateEachLaunch now rotate a saved WorldIdentitySeed too, while
profile IDs remain durable and shared templates keep profiles distinct.
See [new account controls](observations/2026-09-24-late-disconnect-new-accounts.md).

The following pause paragraph is historical:

User requested a pause: they attribute the problem to the accounts and will
create new accounts before resuming. Stop further trials until they return.
The long solo follow-up was interrupted before confirmed world entry and is
not a valid negative control. Last observed: no game processes, desktop
Collector PID 21148. Rediscover all state on resume.

After the 120-second successful release pair, both game PIDs remained alive
but neither had an established connection at 08:43 UTC. The user reported no
manual exit/network change. Coordinates still read successfully after this:
they cannot alone prove a live world. Long solo/paired controls are required
before claiming sustained success. See the latest HANDOFF status.

The earlier rejection has a working, controlled fix. Read
[HANDOFF.md](HANDOFF.md) and the
[new observation](observations/2026-09-24-builder-source-continuation.md)
before the historical journal below.

Offline execution of driver RVA `0x95620` exactly reproduced both successful
solo and failed paired 69-byte packets. The added field comes from a shared
encrypted envelope; positions 8–23 of its decoded 32 bytes stayed identical.
Changing only those 16 bytes passed matched solo PID 6236 and paired PID 18324
controls, with the first Gamma still alive and connected.

The native implementation then passed solo PID 18652 and paired PID 15404,
using normal launcher/GUI-hook entry, no early-root experiment, no syscall
pause, no external emulation, no kernel writes. The current release was
published and launched with `--launch-profiles=Gamma,Black`; both profiles
have distinct PIDs and valid world coordinates. Release verification and the
final observed PIDs are in HANDOFF. Rediscover them before acting.

The 164-byte diagnostic writer was removed. `LU4Probe` was never loaded in
this continuation. Raw research data stays outside the repo in LocalAppData.
Server rejection code and the hardware origin of the stable 16 bytes remain
unknown; do not describe this as complete OS/driver isolation.

## Historical journal — before the successful control

For a concise next-session starting point and a classified record of failed
experiments, see [HANDOFF.md](HANDOFF.md) and [FAILURES.md](FAILURES.md).

As of 2026-09-24, the goal is **not achieved**. Different worlds work on one
PC; the same two accounts work in Gamma from two physical PCs on the same LAN.
On this PC, the second Gamma connection repeatedly closes upstream after the
15/13/69-byte world handshake, with or without its authenticated HTTP proxy.
See `README.md` for earlier controls and the focused 2026-09-24 observations.

The strongest new facts are: (1) a normal elevated launcher under a separate
Windows account enters Gamma alone but fails concurrently; (2) the child
KernelBase `MachineGuid` hook installed too late in the concurrent run and
spoofed zero reads; (3) early `0x222044` output has an eight-byte trailer that
remained identical across three second-client launches, including a rotated
template. The trailer comes from a runtime driver global, but its role in the
world exchange is not known. A second launch path via a full-tree junction
also failed at the same world exchange. Preloading KernelBase before child
resume broke Active Anticheat startup and was removed.

The 2026-09-24 ETW call-stack capture resolved the pre-window registry lead.
The launcher's four and child's seven `MachineGuid` reads came from
NVIDIA/Direct3D graphics or crypto code; six `DevQuery` UUID reads came from
audio enumeration. None of these captured stacks contain `clmods64.dll`.
One direct early `clmods64.dll -> ntdll.dll` read does target the system
`ComputerName` registry value. A pass-through counter found that the module
does not resolve `NtQueryValueKey` through our existing early GetProcAddress
IAT callback (0 of 0x46 resolver calls). See
`observations/2026-09-24-registry-callstacks.md` for exact evidence.

Two controlled Black launches temporarily set the system `ComputerName` to
its fixed template. The 55-second trial held it through the world exchange;
ETW confirmed a direct early `clmods64.dll` read while set, and early RSMB/MAC
replacements were active. Black still failed at the same 15/13/69-byte Gamma
exchange. The registry value was restored and Gamma remained in world. See
`observations/2026-09-24-computername-control.md`. This rules out *that field
alone* as a sufficient fix and removes the immediate reason to implement a
kernel registry callback for it.

The useful next investigation is whether the driver's shared `0x222044`
trailer or another machine-global anticheat signal participates in the
69-byte world follow-up. Do not add another speculative HWID field or mutate
anticheat output without evidence. A server-side rejection code or a
controlled second-PC trace would shorten the search substantially.

The Gamma game PID 12416 was still alive after both ETW captures and the
pass-through probe. Rediscover all PIDs before acting.
The temporary `PriceCheckLab` account is disabled and has a random unknown
password; an earlier automatic review blocked account/credential cleanup.
Automatic command review also rejected removal of two test junctions under
ignored `workspace/`; the installed game tree was not changed.

Validation: native `ClientLaunch/build.ps1` and the `ClientLaunch.LiveSmoke`
Release build pass after removing the failed preload experiment. The root
`build.ps1 -SkipBroker -OutputDirectory workspace\build-verify` also passes;
the staged agent/login DLL hashes match the latest native build. Publishing to
the default release folder failed because the active Gamma process has both
DLLs locked. The active release app has no experimental environment flags and
was left running. The release folder still contains the previous DLLs; do not
mistake it for the latest test build.

The latest `native/ClientLaunch/build.ps1` passes with an opt-in passive
`early_ntquery_resolver_hits` counter. It has not been published to the active
release folder. Same-world dual entry is still unverified.

The next driver-global experiment is recorded in
`observations/2026-09-24-driver-global-perturbation.md`. A read-only driver
global equals the eight-byte `0x222044` trailer and a single `clmods64.dll`
data slot in both clients. Root-only substitution of the second child's
early response trailer, plus a matching write to its user-mode slot held
through the world follow-up, **still failed at the same 15/13/69-byte
exchange**. The fixed `0x22201C` block is twelve zero bytes and one nonzero
word; its whole pattern was absent from live driver/client images. The role
of these values is unproven. Do not treat the experimental trailer branch as
a production isolation feature: it was removed from native source after the
negative test. The normal release DLLs remain unchanged.

Static Ghidra follow-up found that the driver's direct physical-memory import
is part of a generic 4 KiB kernel-memory copy helper, not a demonstrated
SMBIOS read. The root staging build, native build and LiveSmoke Release build
pass; staged agent/login DLL hashes equal the latest native outputs. The
normal Collector PID 9608 and Gamma PID 12416 were still responsive, and the
Collector had an established tunnel to its configured proxy. Rediscover PIDs
and confirm the live connection before another concurrent trial.

The optional comparison with a successful launch on the second physical PC
can use ignored `workspace/two-client-isolation/summarize-early-driver-trace.py`
on an opt-in early trace. It prints only the SHA-256 prefix of the 0x222044
trailer and the existing 0x22201C block fingerprint, not the token bytes or
credentials. The local failed-run summary matches the known driver-global
hash prefix. A second-PC result would establish whether the value differs
between machines; it would not by itself prove the server uses it.

A new matched solo/concurrent control is in
`observations/2026-09-24-solo-trailer-control.md`. The **same eight-byte
replacement** in the second client's `clmods64.dll` slot survived through
the 69-byte world follow-up. Solo Black then received the next 193-byte reply
and entered Gamma; concurrent Black got upstream EOF after 69 bytes while
the first Gamma tunnel remained established. This rules out that one cached
user-mode slot as a sufficient isolation fix. It does not test whether a
correctly recomputed early driver response or other kernel-side state is
needed. Latest restored Gamma PID was 4480, Collector PID 2572; rediscover.

The same note now has a matched **early `0x222044` trailer plus clmods slot**
control. Solo Black PID 19496 entered Gamma with both changes and received
the 193-byte reply; concurrent Black PID 19724 used the same replacement in
both places, reached the 69-byte follow-up and again received upstream EOF
while the first Gamma tunnel remained connected. Root traces verified one
full early response replacement in each run. Therefore the observed pair of
copies is insufficient, and the edit itself does not inherently break solo
entry. The temporary mutation branch was removed; native and LiveSmoke
win-x64 builds pass. Latest restored Gamma PID was 20600, Collector PID
13760; rediscover before acting.

The new matched `0x22201C` control is in
`observations/2026-09-24-01c-session-word.md`. Its last DWORD is copied from
`active64.sys` RVA `0x12ABD4C` to `clmods64.dll` RVA `0x7EFD8`; it varies
between sessions. A temporary root-only early-reply edit and a matching
second-child cache edit stayed in place through the 69-byte follow-up. Solo
Black PID 9656 entered Gamma and got the 193-byte world reply. Concurrent
Black PID 5512 got EOF after 69 bytes with the original Gamma connected.
This pair of copies is insufficient; the edit itself does not prevent solo
entry. The temporary early mutation code was removed. Last normal Gamma was
PID 1592 under Collector PID 19812; rediscover and check its established
50100 tunnel. Next investigate construction of the 69-byte follow-up, using
read-only capture/instrumentation before more field substitutions.

The follow-up send path is documented in
`observations/2026-09-24-world-send-path.md`. Normal solo Black still succeeds
and concurrent Black fails. The game calls `send` with 37 bytes at
`lu4.bin` return RVA `0x16CB803`, but the local proxy broker reads 69 bytes;
the original 37-byte fingerprint matches no contiguous 37-byte window in
that same 69-byte message. Live `ws2_32!send` branches through an executable
private trampoline to `clmods64.dll` RVA `0xB700`. A second hook at
`ws2_32!send+5` confirmed one 69-byte send, with a stack frame in
`clmods64.dll` RVA `0x1633E99`, two milliseconds after the game's 37-byte
send. This confirms the anticheat transformation on this path, though its
inputs and the server rejection key are not decoded. Last checked normal Gamma PID 11908,
Collector PID 11472, established world proxy tunnel. Rediscover PIDs before
another test. The test-only world stack and packet capture flags are opt-in.
In a paired raw-byte comparison (Black PID 16572), none of the 37 source
bytes survived as a contiguous run longer than one byte in the 69-byte
packet; no 32-byte insertion reproduced the result. The temporary raw source
logging and send pause were removed. Native, LiveSmoke win-x64 and root
staging builds pass, and both staged agent hashes match native. The active
release process still uses the previously published package; the current
diagnostic build is under ignored `workspace/build-verify`.

The latest `0x222044` block control is in
`observations/2026-09-24-044-block-map.md`. Early paired captures shared 40
of 76 sixteen-byte block hashes, while initial solo captures shared only 39
with them; the extra difference was byte `0x28D` (`0x58` versus `0x60`). A
strict follow-up disproved using that byte as a live-client flag: after
stopping normal Gamma and waiting until no game process remained, solo Black
and two sequential prelogin Black launches all had the earlier paired
block-40 hash (`0x60`). A temporary root-only byte edit had an ambiguous
startup failure and was removed, along with the temporary raw-response
writer. The opt-in hash map remains read-only. Do not implement isolation
based on this byte. The normal Collector PID 9656 and Gamma PID 1480 were
verified with an established proxy tunnel after the test; rediscover them
before reuse. Native, LiveSmoke win-x64, and root staging builds pass, and
their agent DLL hashes match. No raw reply bytes or credentials are tracked.

Next useful evidence: a server-side rejection reason for the second Gamma
connection, or a controlled trace of the `clmods64.dll` transformation from
the game's 37-byte source send to the 69-byte wire request. The current
world-send stack note records the boundary but not the identity input. The
same two accounts working from two separate physical PCs is user-reported;
it has not yet been reproduced on this machine. Same-world dual entry remains
the required success condition.

The latest world-entry input analysis is in
`observations/2026-09-24-world-ioctl-inputs.md`. Opt-in hooks found the
same three Active Anticheat IOCTL codes near the second-client world exchange.
All sampled 164-, 40- and 59-byte input buffers were unchanged by their
respective `DeviceIoControl` calls. Window fingerprints and a one-time local
raw comparison showed that neither the game's 37-byte source send, the
near-send 59-byte IOCTL input, nor the 40-byte `0x22215C` input is copied as
an intact contiguous field into the 69-byte wire send. In a matched control,
solo Black PID 2176 entered Gamma and returned a radar snapshot; after
restoring normal Gamma, Black PID 16504 failed after the 69-byte send while
the first world tunnel stayed established. The two failed paired input
captures shared almost no byte positions, so a simple stable plaintext
machine field was not found. This is evidence of a transform, not its
algorithm or rejection rule. The temporary raw-input writer was removed;
only opt-in hash-only code remains. Native, LiveSmoke win-x64 and root staging
builds pass and their agent hashes match. Latest normal Gamma PID 20768,
Collector PID 14344, established 50100 tunnel; rediscover before reuse.

Next: identify the `clmods64.dll` path that constructs the 69-byte send and
its inputs, or obtain a server-side rejection reason. A read-only 4-byte
output of `0x222158` remains unclassified. Do not implement a new spoof or
claim dual Gamma success from the current transformed-buffer evidence.

The four-byte `0x222158` output was then sampled once in a successful solo
Black run and twice in failed paired Black runs. All three values differed;
there is no stable literal success/failure code. Its semantics are still
unclassified, and the temporary value-capture code was removed. Latest
normal Gamma PID 18016 under Collector PID 11940 retained an established
proxy tunnel after the repeated paired test. Native, LiveSmoke win-x64 and
root staging builds passed after removal; verify process IDs again before
further experiments.

The opt-in world buffer layout control (Black PID 9808) showed that the
37-byte game source and 69-byte anticheat send use different non-stack
buffers, more than 1 MiB apart. Black still failed at character selection;
the normal Gamma PID 18016 and Collector PID 11940 retained their tunnel.
The full observation is in
`observations/2026-09-24-world-buffer-layout.md`. The producer of the
69-byte buffer and the server rejection rule remain unknown. Prior proxy
tests already showed distinct public exit IPs, and the same accounts work
from two PCs on one LAN; do not attribute this to one shared public IP.

Further paired controls located the 69-byte buffer in `clmods64.dll` `.data`
at RVA `0x7F01D`, while the original 37-byte source is in private memory.
In three paired Black runs, the slot changed between entry and return of
the **second** `DeviceIoControl(0x222160)` immediately before the world
send; its post-call hash matched all 69 sent bytes. A byte-difference count
in PID 1616 found 68 of 69 slot bytes changed across all nine eight-byte
blocks. The first and third `0x222160` did not alter the slot. No direct
64-bit pointer into the module image was present in the observed 164-, 40-
or 59-byte driver inputs. See `observations/2026-09-24-world-buffer-layout.md`.
This localizes packet construction to the IOCTL call boundary but does not
show its algorithm, hardware inputs or the server's reason for closing the
second Gamma connection. The diagnostic code is opt-in; normal Collector
PID 11940/Gamma PID 18016 kept its established tunnel after these tests.

A successful solo Black control (PID 15576) then produced the same
`0x222160` transition: 68 of 69 bytes changed in the `clmods64.dll` slot,
and its post-call hash matched the send. The normal Gamma client was
restored as PID 19736 under Collector PID 10440 with an established proxy
tunnel. The transition is therefore normal protocol behavior, not a branch
unique to concurrent failure. Neither the direct input scan nor three
earlier raw 59-byte samples contained a plain user-mode pointer to the
slot. Next work needs to resolve the active64.sys handler's input sources
or obtain a server-side rejection reason; modifying the slot or guessing a
hardware field is not justified. Same-Gamma dual entry remains unverified.

A paired Black PID 15092 also ran with an opt-in `NtDeviceIoControlFile`
boundary hook. The same 69-byte slot changed *inside* the second
`0x222160` syscall boundary; the post-syscall hash matched both the outer
`DeviceIoControl` return and the send. The first and third `0x222160` left
it unchanged. This is consistent with an Active Anticheat driver-side
write, though an exactly concurrent writer is not excluded. The first
Gamma under Collector PID 10440 / child PID 19736 retained its tunnel.
The precise driver algorithm and machine input are still unclassified.

The newest result is in
`observations/2026-09-24-world-buffer-layout.md` under **Kernel record copy**.
The driver's `0x222160` handler selects a per-process record by EPROCESS.
During the second world IOCTL, its record `+0x4068` becomes an exact copy
of the 69 bytes sent from `clmods64.dll` RVA `0x7F01D`. Static decoding found
user-mode copy-in/copy-out paths for that record buffer and an RC4-style
stream transform using state at record `+0x240CC + index*0x7B8`. In four
controlled paired captures the relevant state advanced exactly 67 bytes.
Reversing that stream over packet bytes 2–68 recovered the game's original
37-byte request payload at positions 2–36, followed by a newly generated
32-byte field. The two-byte header also changes. This explains the 37-to-69
repackaging, but the added field and the server's duplicate-machine rule
remain unknown. The second Black child still failed after this packet in
every paired run, while normal Gamma PID 20936/Collector PID 19980 held its
proxy tunnel; rediscover these PIDs before use.

Further controls found that the recovered 32-byte field differed between
two paired runs with the same Black account/template/proxy; it is not a
constant plaintext HWID. Two original 37-byte Black requests matched only
at header positions 0 and 1 plus one isolated byte at position 18. There
is no evident contiguous fixed machine ID in that request either. The
current evidence does not establish whether the 32-byte field depends on
hardware, a server challenge, or both. A diagnostic capture initially
missed a second client in driver record slot 2; the local capture helper
now chooses the active pre-world record by population and verifies its
post-call state. The missed capture is excluded from comparisons.

Next: trace the added 32-byte field's inputs through the obfuscated helper
at `active64.sys` VA `0x140095620` (called from `0x1400615C9` with the
per-process context and record `+0x4068`). The helper's on-disk exception
range is `0x140095620`–`0x1400A3E73`; Ghidra cannot decompile the flattened
CFG directly. Do not assume the 32 bytes encode HWID until a controlled
input/output comparison or static data-flow trace establishes that. Same
Gamma dual entry remains the success condition.

The static call inventory of the two flattened functions (`0x140095620` and
context initializer `0x14002C780`) found local cryptographic, copy and
status helpers, with no direct imported hardware/registry query in these
paths. This does not exclude a hardware-dependent value supplied earlier.
The root `build.ps1 -SkipBroker -OutputDirectory workspace/build-verify`
passes. Native, LiveSmoke and staged `PriceCheck.ClientAgent.dll` SHA-256
hashes agree. `git -c core.safecrlf=false diff --check` passes. The normal
Collector PID 19980 still had an established proxy tunnel and Gamma PID
20936 remained in world after the controlled runs; rediscover before reuse.

A final matched solo Black run (PID 7988) entered Gamma while the original
Gamma was temporarily stopped. It showed the **same** 67-byte state advance,
35-byte original body and 32-byte added field as the failed paired runs.
This rules out a missing anticheat call or different packet layout in the
concurrent case. The script restored normal Gamma as game PID 13624 under
Collector PID 1864, verified with an established proxy tunnel. A second
`PriceCheck.Collector` process with no window is that collector's
`--upload-worker` child, not an orphaned desktop instance.

A read-only loaded-image scan then found the physical Bluetooth adapter's
six-byte MAC once in `active64.sys` at RVA `0x12ABD56` and once in the first
Gamma `clmods64.dll` at RVA `0x7EAEA`. The template MAC was absent as an
exact six-byte sequence in the client module. See
`observations/2026-09-24-bluetooth-mac.md`. This is an unspoofed hardware
copy, not yet proof that the server's same-world rejection uses it. Test a
disposable second client's cached copy with matched solo/paired controls;
leave the active driver's global and first Gamma process untouched.

The controlled cached-MAC test is now complete. Paired Black PID 20772
kept the Black template MAC in its `clmods64.dll` slot through the 69-byte
world request but still failed, with the first Gamma tunnel connected.
Solo Black PID 16704 entered Gamma with the **same slot edit** and returned
a radar position. The one cached user-mode MAC copy is therefore
insufficient as a dual-client fix, though the driver's machine-global MAC
and derived values were not changed. Normal Gamma was restored as Collector
PID 20964 / game PID 6576 with an established world proxy tunnel. See the
Bluetooth MAC observation for detail; rediscover all PIDs before tests.

The early driver response exposed a stronger path: the physical Bluetooth
MAC occurs at offset `0x96` in the `0xB0`-byte `0x22201C` reply, and in none
of three sampled `0x222044` replies. An opt-in test-only edit of the six
reply bytes made the second Black client's `clmods64.dll` cache equal its
template MAC before login and after the 69-byte request. Paired Black PID
15356 still failed after 69 bytes; solo Black PID 15804 with the **same early
edit** received 193 bytes and entered Gamma. This rules out that early
reply/cache path alone as a sufficient isolation fix. Normal Gamma was
restored as Collector PID 9668 / game PID 936, established proxy tunnel.
See the Bluetooth MAC note. The temporary mutation branch was removed;
only opt-in read-only diagnostics remain. Native, root staging and LiveSmoke win-x64 builds
pass; `git diff --check` passes. Current normal Gamma is Collector PID 9668 /
game PID 936, and its proxy tunnel was established after restoration.
Rediscover all PIDs before acting.

The separate `LU4Probe` test driver then performed a guarded, six-byte
compare-and-swap on the loaded `active64.sys` global at RVA `0x12ABD56`.
Both drivers' reads agreed and a no-op write succeeded. A first paired
attempt was invalid: the original Gamma process was already gone before
the Black harness could start. A later probe-driver load also closed Gamma
without a MAC edit, and the user confirmed that driver loading closes game
windows. Do not attribute that exit to the global edit. In two solo Black
controls, a prelaunch global edit had been undone by prelogin: the physical
MAC returned to `active64.sys` RVA `0x12ABD56` and the client cache. The
second run scanned the full loaded image and found exactly one physical
MAC occurrence, at that original RVA. Thus new client startup repopulates
the global from hardware. The original value was verified and normal Gamma
restored as Collector PID 13520 / game PID 7552, established proxy tunnel.
See `observations/2026-09-24-active64-global-mac-control.md`. Load the
probe before starting the first game; edit the refreshed global only after
the second game's anticheat initialization, then test simultaneous entry.
The full same-server two-window objective is still unverified.

A valid paired control followed after loading `LU4Probe` **before** the first
Gamma client. Black PID 20756 paused after anticheat startup. Its
`clmods64.dll` cached MAC and the refreshed active64 global MAC were both
changed to the Black template, and both changes persisted through the
69-byte world send. Black still received upstream EOF; first Gamma PID 7552
and its world proxy tunnel survived. The original global was restored.
This rules out those two raw MAC copies together as a sufficient isolation
fix. Other driver state or earlier snapshots remain untested. See the
active64 global MAC note.

Three complete early `0x22201C` replies were then compared: two paired
Black probes and one successful solo Black control. The driver-global six
bytes at reply offset `0x90` were identical in all three, and the physical
MAC at `0x96` was also identical. An opt-in guarded substitution of just
the `0x90` six bytes was tolerated in solo Black PID 9364 but concurrent
Black PID 20704 still failed immediately after its 69-byte world request,
with the first Gamma tunnel intact. The mutation branch was removed after
the negative control; read-only raw capture remains opt-in. See
`observations/2026-09-24-early-01c-layout-and-six-byte-control.md`.
The normal Collector/Gamma was restored as PID 20324 / PID 12800 before the
paired test; rediscover current PIDs before further work. Same-world dual
entry remains unverified.

The separate probe driver was then rebuilt with a guarded six-byte CAS
whitelist for active64 RVA `0x12ABD50` as well as the earlier MAC slot.
The original Gamma/Collector were stopped before loading the updated probe,
then normal Gamma was relaunched. Paired Black PID 7164 kept the new global
six bytes through its 69-byte world send, but still received upstream EOF;
first Gamma stayed connected. A matched solo Black PID 10400 kept the **same
replacement** through world send and successfully entered Gamma. The
original global was restored after each test. Normal Collector/Gamma was
restored as PID 1052 / PID 14372 with an established proxy tunnel, and both
probe-exposed original slots were verified. See the early-01c layout note.
The next causal control, if this field is pursued, must edit both the early
`0x22201C` response and the refreshed driver global with the same value.

That combined control is now complete. Paired Black PID 5140 had one
verified early-reply edit and the matching global still changed at its
69-byte send, but failed with upstream EOF while first Gamma stayed
connected. Matched solo Black PID 18988 entered Gamma with the same two
edits. The original global was restored; normal Collector/Gamma was
restored as PID 20968 / PID 15780 with an established tunnel. The temporary
early mutation branch was removed. The shared six bytes at RVA `0x12ABD50`
are not a sufficient isolation fix, even when both observed paths agree.

The adjacent eight-byte `0x222044` trailer source at active64 RVA
`0x12ABD68` was then tested with a separate guarded test-only CAS IOCTL.
Global-only and global-plus-child-cache matched solo/paired controls
showed the same split: solo Black entered Gamma, concurrent Black closed
after the 69-byte request despite verified replacements. A final control
changed the early `0x222044` reply, the child cache and the driver global
to the same recorded value. Paired Black PID 19924 still failed with
upstream EOF and first Gamma connected; solo Black PID 9936 entered Gamma.
All driver edits were restored. Normal Collector/Gamma was restored as
PID 6736 / PID 14020 with an established tunnel. The temporary early
response mutation code was removed again. See
`observations/2026-09-24-044-global-eight-control.md`.

The tested shared `0x22201C` and `0x222044` copies are insufficient for
two same-Gamma clients. The next evidence needed is the server/anticheat
rejection reason or a matching successful trace from a second physical PC.
Further blind edits of nearby globals would not identify the rejecting
condition. Two simultaneous Gamma world entries remain unverified.

Cleanup/validation after the eight-byte controls: the three inspected
active64 globals were restored and verified; the separate `LU4Probe`
service was stopped and deleted. Normal Collector PID 6736 and Gamma PID
14020 remained alive with an established 50100 proxy tunnel. The root
`build.ps1 -SkipBroker -OutputDirectory workspace/build-verify`, ordinary
`native/LU4Memory/build.ps1`, LiveSmoke win-x64 build, staged agent hash
comparison and `git diff --check` all passed. The active release app was
not republished while the game held its DLLs open. Rediscover PIDs on resume.
