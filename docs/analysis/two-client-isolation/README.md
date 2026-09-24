# Two simultaneous LU4 clients: world-session rejection (2026-09-23)

**2026-09-24 final: observed pair succeeded; monitoring stopped by the user.**
09:35:28–12:13:48 UTC: 951 checks over 2 h 38 min, no new world EOF,
914 fully healthy samples, 35 skipped coordinate checks and 2 transient
metadata backlogs. Both games remained connected after the monitor stopped;
automation `gamma` is paused. This is not a completed 24-hour test. See
[monitor evidence and limits](observations/2026-09-24-long-monitor.md).

**2026-09-24 latest: 7-minute solo and 10-minute pair passed.** New accounts
and regenerated HWIDs/world seeds work with the restored original proxies.
The older two-minute trial later disconnected; its reason remains unknown.
See [new long controls and periodic traffic](observations/2026-09-24-late-disconnect-new-accounts.md).
Matched solo
and paired controls isolated a stable 16-byte part of the added request field.
The normal agent now supplies a distinct saved-profile identity there, with
driver/layout/integrity guards. See the current [handoff](HANDOFF.md) and
[source/control evidence](observations/2026-09-24-builder-source-continuation.md).
The entries below are the earlier chronological record; their “unverified”
status predates this result.

For the 2026-09-24 handoff, start with the [failed-attempt index](FAILURES.md)
and [next-session handoff](HANDOFF.md). They distinguish matched negative
controls from invalid or ambiguous tests. This page is the chronological
research log.

Latest 2026-09-24 controls: guarded edits of the early `0x22201C`
six-byte driver value, and of the `0x222044` eight-byte trailer in its
early response, child cache and driver global, were each tolerated by
successful solo Black entries but did not let a second Black client enter
Gamma while the first stayed in world. The test-only driver was unloaded;
normal Gamma was restored. See `RESUME.md` and the focused observations
for the exact matched controls. Two simultaneous Gamma world entries are
still unverified; the server/anticheat rejection reason is not yet known.

Latest 2026-09-24 control: the second client's `0x222044` driver reply has a
stable-looking block near offset `0x28D`, but its differing byte is **not a
reliable count or flag for another open client**. A matched solo/two-prelogin
sequence held the same block hash throughout. A temporary single-byte edit
did not pass the solo startup control and was removed. See
`observations/2026-09-24-044-block-map.md`. The conclusive network symptom
remains the anticheat-transformed 69-byte world request followed by upstream
EOF only for the second same-Gamma client. Two simultaneous Gamma entries
remain unverified.

The next no-content world-entry probe and matched successful-solo/failed-paired
control are recorded in `observations/2026-09-24-world-ioctl-inputs.md`.
Active Anticheat IOCTL inputs remain unchanged across each call. The game's
37-byte send, the near-send 59-byte driver input, the 40-byte challenge input
and the 69-byte wire message do not contain one another as complete unchanged
fragments. These inputs vary almost entirely even between two failed launches
with the same saved template. This narrows the dataflow but does not expose
the server's rejection criterion.

The [world buffer layout probe](observations/2026-09-24-world-buffer-layout.md)
confirmed that the game's 37-byte source and the anticheat's 69-byte send
use separate buffers, more than 1 MiB apart and neither on the current
thread's stack. Follow-up checkpoints located the wire buffer at
`clmods64.dll` `.data` RVA `0x7F01D`; 68 of its 69 bytes changed during the
second `0x222160` driver call, and its post-call hash matched the sent packet.
A successful solo Black control showed the same 68-byte transition, so it is
part of the normal world protocol. This narrows the transformation path
without revealing the machine-derived input or enabling a second Gamma
world entry.

The Collector had Gamma PID 16828 in the world while the saved Black profile was launched separately. Black used its own launch template, account, fixed generated identity, authenticated HTTP proxy username, WFP listener and final PID. The templates differ in all 20 stored identity fields. Independent HTTPS requests through the two saved proxy credentials reported different public exit IPs (`external-address-4` and `external-address-5`). This does not prove that every game-visible identifier is distinct.

With Gamma still in the world, Black PID 2468 reached agent readiness and a driver radar snapshot. Its proxy returned HTTP 200 for the login and world CONNECT requests. On port 7782, the game sent 15 bytes, received the initial 13-byte world reply, sent the next 69-byte request, and the tunnel ended before the next world reply. Programmatic login timed out while waiting for character selection. A repeat with a new proxy-side EOF trace (Black PID 10968) showed the upstream proxy/world connection closed first, about 1.8 seconds after the 69-byte request. The game screen showed “You have been disconnected from the server” over the server selection UI. Raw packet contents, credentials and session tokens were not logged.

The same Black profile and hardware template with the proxy disabled (PID 18892) failed at the same server selection screen while Gamma remained in the world. Therefore HTTP proxy authentication and the WFP relay are not the sole cause. Earlier, with no other game client active, this Black profile and its saved proxy entered the world successfully (see `../wfp-proxy/README.md`). The live two-client smoke that only launched both processes did not test simultaneous account/world entry.

The native `ClientLoginService` reports status 3 immediately after invoking the server-selection function, then waits for a character-selection object. Status 3 is not confirmation from the world server. The second client's timeout therefore reflects the server disconnect, not a successful character choice. Existing per-process HWID hooks install after the game window appears; an earlier experiment observed two SMBIOS reads before that point. Whether the server decision is based on this earlier physical identity, a different unhooked identifier, linked account policy, or another condition remains unproven. Do not add a speculative hook or claim full client isolation from the 20 template values.

The user reported that the same two accounts also fail to coexist through HardShift on this PC, **but do enter simultaneously from two separate physical PCs on the same network**. This rules out a blanket account-pair concurrency ban and a simple shared public-IP limit. It does not identify whether the server uses a hardware identifier, an anticheat service signal, client-local shared state, or another same-PC observation. The HardShift configuration currently has one client bound to `Random` and another with no template, so its unsuccessful run does not demonstrate two distinct HardShift identities. Further work should instrument only an identity surface supported by evidence. Keep the user's existing Gamma PID untouched during further experiments unless the user agrees to stop it.

An additional Black test used separate `LOCALAPPDATA` and `APPDATA` environment directories under ignored `workspace/isolation-profile-black/`. The agent wrote its trace there, proving the variables reached the child, but LU4 continued to use the original Windows Known Folder for its `Saved` configuration. World entry still failed. Merely overriding these environment variables is not a valid per-profile data isolation mechanism for this client. The harness terminated only its second test PID after the timeout.

## Early SMBIOS interception trial

The normal GUI-hook agent is installed after the game window starts. Earlier opt-in traces showed two `GetSystemFirmwareTable('RSMB')` calls before that point. To test whether covering them is viable, an experimental root `CreateProcessW` hook briefly resumed the final child before loading the agent, using 0, 500, and 1000 ms delays. Each second-client trial reported agent readiness and intercepted the two SMBIOS calls about one second later, but the protected client exited before a stable game window during Active Anticheat checks. PIDs 10884, 1092 and 16772 were test-owned and are gone; Gamma PID 16828 was not stopped. This matches the earlier finding that early DLL presence itself is incompatible with this client. The optional resume-before-inject change was removed from source after the trial. It does not establish that SMBIOS alone causes the server's two-client rejection.

A read-only import inspection of the local Active Anticheat driver showed generic process, file, registry and system-information imports, including `ZwQuerySystemInformation`; it did not expose a specific fingerprint field or rejection code. No anticheat driver or machine-wide identity was modified.

## Driver requests during world entry

An opt-in `DeviceIoControl` trace was added to the existing client agent (`PRICECHECK_TRACE_HARDWARE=1`); the normal path does not write this trace. In a further second-client Black trial (PID 16080), the proxy accepted the world TCP connection at 18:19:22.486. The agent then observed IOCTL `0x222158` at 18:19:22.487 and `0x222160` at 18:19:22.489. IOCTL `0x22215C` followed the first 13-byte world reply at 18:19:23.509; `0x222160` occurred after the game's 69-byte send at 18:19:23.539 and again when the remote connection closed at 18:19:25.289. This trial also failed at server selection. The trace is local to the test PID and contains no account credentials or packet contents.

The three IOCTL numbers appear as literal comparison operands in the local `active64.sys` driver, in one obfuscated dispatch function at image address `0x1401193b0`. Ghidra's static analysis found `0x222158` once and `0x22215C`/`0x222160` twice each in that function. Their timing and presence in the driver support that the anticheat participates in this world-entry exchange. They do **not** identify the requested data, the exact rejection reason, or a safe per-process isolation method. No driver bytes or anticheat state were changed.

The three dispatch branches use an indirect jump table, with entry offsets `0x1088` (`0x222158`), `0x88` (`0x22215C`) and `0x80` (`0x222160`). Ghidra resolves their first targets to `0x14011eab6`, `0x14011f1af` and `0x14011ee68`, respectively, but the subsequent control flow is deliberately flattened. Static disassembly has not established whether those requests return hardware data or only manage the anticheat's session state. Avoid treating the matching timing as proof of a particular HWID check.

## IOCTL outcome and buffer sizes

The agent's opt-in trace now records only the input/output byte counts, call success and Win32 last-error value for those three IOCTLs in a separate `ioctl-trace-<pid>.csv`; it records no buffer contents. The hook also restores the `GetLastError` value from the real `DeviceIoControl` call after tracing, so logging cannot replace the caller-visible error code. A first attempt ran against a stale agent DLL copied into the smoke-test output and produced no new trace; it was stopped and the native agent was built and copied before repeating.

In the repeated Black test, PID 14424, with Gamma PID 16828 still in the world, `0x222158` passed 164 input bytes and requested a 4-byte output (4 bytes returned). `0x22215C` passed 40 input bytes with no output, and each `0x222160` passed 59 input bytes with no output. **All five calls returned success with last-error zero**, yet the Black client again timed out waiting for the character list after world selection. This rules out a failing local `DeviceIoControl` return as the immediate cause. The 4-byte output's meaning, buffer contents and server-side decision remain unknown. Only the second test PID was stopped; the first live client was left running.

Those observed lengths match comparisons in the driver's three dispatch branches: `0xA4` for `0x222158`, `0x28` for `0x22215C` and `0x3B` for `0x222160`. The `0x222158` branch also checks for at least four output bytes. This cross-check supports that the traced calls reached the expected driver protocol paths; it still says nothing about whether the 164-, 40- or 59-byte inputs contain a machine identifier.

## Client-side caller attribution

An opt-in return-address and stack trace showed that **all three calls originate in `clmods64.dll`**, at return RVAs `0xB5CE`, `0xB63F` and `0xB68F` for IOCTLs `0x222158`, `0x22215C` and `0x222160`, respectively. The following frames are in the same module and then in `lu4.bin`; the client agent is only the logging hook. The trace also recorded stack frames at `clmods64.dll` RVAs `0x162E08B`, `0x162E261` and `0x162E2CB`. This identifies the module responsible for these driver calls, but does not establish which upstream code assembled the contents of the input buffers.

`clmods64.dll` has a `.text` section at RVA `0x1000` with a virtual size of 303,520 bytes but **zero raw bytes in the file**. Several other large virtual sections likewise have zero raw bytes. Ghidra therefore cannot disassemble the live call RVAs from the on-disk DLL. An opt-in, test-only read of the already loaded module's `.text` section produced a 303,520-byte local snapshot. Disassembly of that snapshot verifies three wrappers: `0x222158` sends exactly `0xA4` bytes, provides a four-byte output buffer, and copies that output DWORD to its caller when four bytes return; `0x22215C` sends `0x28` bytes without output; `0x222160` sends `0x3B` bytes without output. All three wrappers return whether `DeviceIoControl` succeeded. No buffer contents were saved.

Additional opt-in snapshots of 4 KiB pages containing the higher stack frames show heavily flattened or transformed instructions. Linear disassembly at those return addresses does not reliably reconstruct the buffer-building logic. These memory snapshots were taken only from the second test process and retained under ignored `workspace/two-client-isolation/`; they were not written into the repository, and the first client and anticheat driver were not modified. The next evidence needed is either a server/anticheat rejection code or a controlled comparison of the relevant buffers between a successful single-client login and a failed concurrent login. The current trace proves a successful local driver call, not the server's reason for closing the connection.

Two further failed Black launches used the **same saved identity template and proxy**. Temporary opt-in fingerprints of the complete 164-, 40- and 59-byte IOCTL inputs differed between the two runs; the four-byte output from `0x222158` also differed. This establishes run/session variation in the protocol and prevents treating equality of the entire buffer as a test for a fixed HWID. It does not exclude stable identifier fields inside those buffers, nor establish what the four-byte output represents. No raw input buffer or account credential was logged. The temporary fingerprint/output recording and module-memory snapshot code were removed from the product agent after this comparison; only sizes, success, caller module/RVA and stack addresses remain in opt-in diagnostics.

For PID 9004, the ordinary identity trace saw four `GetAdaptersAddresses` results before world selection, each with two adapter MAC replacements. It saw no SMBIOS, registry, volume, disk-storage or `SendARP` calls through the instrumented APIs after agent readiness and during the world IOCTL exchange. This only describes those API hooks; it cannot rule out earlier reads, cached values, direct driver queries or other interfaces. In particular, the previously observed early SMBIOS reads occur before the normal GUI-hook agent starts.

## Controlled solo Black comparison

With the user's authorization, Gamma PID 16828 was stopped and its profile store entry was allowed to clear before launching Black alone. Black PID 1864 used the **same saved account, fixed template and authenticated HTTP proxy** as the failed concurrent trials. It passed account login, Gamma selection and character selection; 15 seconds later the radar reported a player position, and the proxy tunnel continued carrying world traffic for more than a minute. The test-owned Black process was then stopped. This confirms the current Black configuration can enter the world on this PC when Gamma is absent.

The decisive difference is immediately after the world connection's first 15-byte client request, 13-byte reply and 69-byte client follow-up. In failed concurrent PID 7996, the upstream side closed before another game reply. In successful solo PID 1864, the upstream sent **193 more bytes**, the client answered with **141 bytes**, and the session continued. The proxy returned HTTP 200 in both runs. Before the first world IOCTL, both traces recorded four `GetAdaptersAddresses` results with two MAC replacements each. Both runs' `0x222158`, `0x22215C` and `0x222160` IOCTLs had the same input/output sizes and succeeded locally. The successful session naturally made many more subsequent `0x22215C` calls. These observations narrow the failure to the remote world exchange, but do not reveal the field or rule behind the remote close.

After the solo test, `build.ps1` published the current Collector and it was started with `--launch-profile=Gamma`. The replacement Collector PID 1184 automatically launched and bound Gamma PID 18264. The saved Gamma profile recorded the new PID, and the game maintained an established TCP connection through the Collector's own listener to its configured proxy. This startup option is a recovery and automation aid; it uses the same profile launch path as the Launch client button.

## World handshake packet shape

An opt-in, test-only broker capture saved the first short world packets under ignored `workspace/two-client-isolation/packet-capture/`. With Gamma active, Black PID 11020 repeated the failure after a 15-byte client request, 13-byte server reply and 69-byte client follow-up. After stopping Gamma, Black PID 10836 entered the world and received a further 193-byte server reply before sending 141 bytes. No account or proxy credentials were printed or committed. The capture hook was removed from source immediately after the comparison.

The two runs' 15-byte client requests were **identical at all 15 offsets**. The 13-byte server replies matched at five offsets (the first four and last byte). The two 69-byte client follow-ups matched only at their first two offsets. Thus the 69-byte body is heavily session-dependent in these captures; comparing the entire request cannot identify a stable physical-machine field. This does not prove encryption or show whether the request contains an HWID-derived value. The world server's own rejection reason remains the missing evidence.

`build.ps1` was run again after removing the capture hook. The Collector was restored as PID 3688, which launched and bound Gamma PID 17416; the game-to-Collector loopback TCP and Collector-to-proxy TCP connections were established. Discover current PIDs before future tests.

## Driver-input variability

A final opt-in, test-only capture compared the three IOCTL input buffers in failed concurrent Black PID 17096 and successful solo Black PID 8596. Their lengths stayed at 164, 40 and 59 bytes. At the corresponding first `0x222158`, first `0x22215C`, and first two `0x222160` calls, **zero byte positions matched** between the two runs. Even the three 59-byte `0x222160` buffers within the failed run shared only their first four positions consistently. No complete generated template value or physical SMBIOS UUID appeared verbatim in these input buffers as ASCII, UTF-16 or GUID bytes. The buffers have substantial per-call variation, so direct byte equality cannot isolate a machine field. This also means the captured bytes cannot be treated as proof that the hardware identity is absent: it may be transformed, hashed, encrypted, cached earlier, or supplied through another path.

Only the test client's own calls were captured; the real `DeviceIoControl` calls and output were left intact. The raw capture was used locally for the equality and verbatim-value checks. The temporary capture hook was then removed from native source. No driver or game binaries were patched.

After `build.ps1` rebuilt the native agent without the capture hook and republished Collector, Collector PID 19016 launched and bound Gamma PID 16264. The game remained alive with an established Collector-to-proxy connection. Temporary packet and IOCTL binaries are still in ignored `workspace/two-client-isolation/`: the automatic command review rejected a verified-path removal command, so they were left untouched. They are not part of the release or Git-tracked files.

The user further clarified that two clients on this PC can enter **different game servers** at once; the failure occurs when both enter Gamma. This rules out a blanket one-process-per-machine restriction. It is consistent with a server-specific duplicate-device policy, but the responsible field and decision point remain unconfirmed. The current programmatic login UI is still validated only for Gamma, so this different-server observation comes from the user's manual test rather than this harness.

The 2026-09-24 continuation is split into focused notes: [early registry and
separate-account controls](observations/2026-09-24-registry-account.md) and
[driver-output comparison](observations/2026-09-24-driver-output.md). The
[resume note](RESUME.md) states the current strongest evidence and next step.

The later [registry call-stack capture](observations/2026-09-24-registry-callstacks.md)
attributes apparent early `MachineGuid` reads to graphics/crypto and identifies
one direct early `clmods64.dll` read of the system `ComputerName` value.
The [controlled ComputerName trial](observations/2026-09-24-computername-control.md)
held the second template's name through the Gamma world exchange; the same
server-side close still occurred, and the system value was restored.

An opt-in early-agent test then attributed both pre-window `GetSystemFirmwareTable('RSMB', 0)` calls to `clmods64.dll` at return RVAs `0xA711` and `0xA735`. Read-only disassembly of an earlier live module snapshot shows the first call obtains the SMBIOS buffer size and the second retrieves the table. The early-loaded test client exited during startup, as in prior trials; the live Gamma client was not interrupted. See `../early-agent/README.md`.

## Early SMBIOS isolation trial

An experimental root-only launch now loads our agent into the original launcher but skips the early DLL in protected `lu4.bin`. The root process installs a test-only anonymous IAT resolver callback in the child, targeting only `GetSystemFirmwareTable` as resolved by `clmods64.dll`. A callback that forwarded the original API unchanged counted the two early `RSMB` calls and left the child alive past 25 seconds. A second callback variant built a same-size RSMB snapshot from the selected template's UUID, processor ID, system/board/chassis/CPU/memory serials and copied it into the successful firmware response. A standalone child test confirmed ten fields patched, two RSMB calls routed and one response replaced while the child stayed alive.

For a two-client Gamma trial, the Collector launched Gamma normally and Black with the root-only early path. The first run's module finder timed out after about five seconds and Black failed as before; this run does **not** test spoofing. The finder wait was extended. On the next run, Black root PID 12796 found `clmods64.dll`, installed the callback, and recorded two RSMB calls with one spoofed full-table response before the normal GUI agent and proxy became active. Gamma PID 2712 was in the world. Black's authenticated proxy returned HTTP 200 for login and world connections, but the world stream again ended after the 15-byte client request, 13-byte reply and 69-byte follow-up, with remote EOF about 2.7 seconds later. Black was cleaned up by the Collector; Gamma remained alive. Therefore **covering the early SMBIOS API is insufficient**. It does not identify the remaining shared signal or prove which part of the anticheat/server rejects the second session.

The early IAT route is still gated by `PRICECHECK_EARLY_ROOT_ONLY_PROFILE` on the Collector and the root-only child flags. It uses offsets verified only on this local runtime module and intentionally leaves its anonymous code/data and import redirect in the test-owned child until exit. Keep it disabled for ordinary users and do not present this experimental path as complete isolation.

## Additional early hardware paths

Static inspection of the runtime `clmods64.dll` image found a direct `GetAdaptersInfo` import at its internal IAT RVA `0x4C030`. Its function at RVA `0xA5F0` reads six bytes of the first nonzero adapter MAC from the returned `IP_ADAPTER_INFO`. The normal agent previously hooked only `GetAdaptersAddresses`. An opt-in anonymous IAT callback now replaces the early `GetAdaptersInfo` result with the template MAC, and the normal post-window agent also hooks this API. A standalone test counted one early call and one modified response with the child alive after 25 seconds.

In a subsequent two-client Gamma trial, Black root PID 11440 confirmed both early SMBIOS and MAC replacements. Gamma PID 15328 was in the world. Black PID 15648 still received remote EOF after the same 15/13/69-byte world handshake. Thus those two early fields together are insufficient.

The module also contains a legacy ATA SMART path: it opens `\\.\PhysicalDrive%d`, sends `SMART_GET_VERSION` (`0x74080`) and, if supported, `SMART_RCV_DRIVE_DATA` (`0x7C088`), then extracts the 20-byte ATA serial at output offset `0x24`. The prior disk hook handled storage-property and drive-layout queries, not SMART. An opt-in early IAT callback and a normal-agent hook now prepare the template disk serial in ATA word order. In this PC's standalone startup, however, the early trace saw `0x74080` with a 24-byte output and **no `0x7C088` call**. The two-client trial with this change (Black PID 17292, Gamma PID 2620) still failed at the same world exchange; the late agent recorded no SMART serial replacement. This path is a coverage improvement for machines with legacy SMART support, not evidence that the disk serial caused this PC's rejection.

The same early trace observed three `active64.sys` `DeviceIoControl(0x222044)` calls, each requesting 0x4BA output bytes, and one `0x22201C` call requesting 0xB0 bytes before the game window. Their contents and role in the world-session decision remain unverified. These driver-returned data are a more concrete remaining source than another speculative user-mode field.

## Early driver-output classification (2026-09-24)

The root-only test hook was extended behind `PRICECHECK_EARLY_044_CAPTURE=1` to keep up to eight early `0x222044` responses and one `0x22201C` response in the **test child's memory** until the launcher reads them. The trace writes only lengths, caller RVAs, byte statistics and 64-bit FNV fingerprints; it does not save those driver buffers or account credentials to disk. The client stayed alive after each 18- to 25-second standalone probe. Ordinary Collector launches do not set the capture flag.

Across repeated launches, `0x222044` first returned one byte on two to four calls, then returned a full 0x4BA-byte block. The early one-byte results were identical across tests; the full block changed on every tested launch, including two runs with the same specified template values. All calls came through `clmods64.dll` caller RVA `0x162E31F` into its wrapper at RVA `0xB6A0`. The 0x4BA-byte block had roughly random-looking printable/zero counts; this is an observation, **not** proof of encryption or HWID content. It would be invalid to substitute arbitrary bytes into that response or infer that the repeated one-byte polls are three separate hardware reads.

The `0x22201C` call succeeded and returned the requested 0xB0 bytes. Its full-response fingerprint also differed between two launches with the same fixed template. A diagnostic branch initially missed this call because the `0x222044` skip jumped past it; that instrumentation bug was fixed before the comparison. The outputs' exact semantics and any server-visible machine field remain unknown. No game or anticheat driver output was changed during these probes.

## Separate Windows-account control

A temporary `PriceCheckLab` local account was used to test stronger per-user isolation. A direct `lu4.bin` launch under that account reached a visible game window in the same desktop session after explicitly setting its own `USERPROFILE`, `APPDATA`, `LOCALAPPDATA` and `TEMP`. A test helper running as that account loaded `PriceCheck.ClientLogin.dll` and invoked its window hook. With Gamma already in the world, the second account reached login status 2 and server-selection status 3, then ended at `-24` (no character list). **The same direct launch also ended at `-24` with Gamma stopped.** Therefore this direct-binary test cannot determine whether a separate Windows profile solves the two-client conflict: it fails the single-client control.

The normal `lu4-win64-shipping.exe` declares `requireAdministrator`, and `Start-Process -Credential` initially failed with Windows error 740. Running it with `__COMPAT_LAYER=RunAsInvoker` created a launcher process but no child `lu4.bin` during a repeat observation longer than 150 seconds. One earlier delayed child was seen with that launcher's parent PID, but it was not validated as a working client; its timing could not be reproduced. The local account experiment must not be presented as a successful isolation method. The test-owned processes were stopped, and the ordinary Collector/Gamma startup was restored after the control test.

The test user's Windows profile was removed through `Win32_UserProfile` after confirming the resolved path was exactly `C:\Users\PriceCheckLab` and the profile was unloaded. Automatic command review then rejected a combined deletion of the temporary local account and its DPAPI-encrypted credential file, with no more specific reason. The account was disabled and its password changed to a fresh, unrecorded random value; the saved test credential can no longer authenticate. Collector PID 15536 relaunched Gamma PID 8556 in normal mode; the window title showed the character `Grader`, with an established Collector proxy connection. Rediscover PIDs before further work.

The latest [driver-global perturbation](observations/2026-09-24-driver-global-perturbation.md)
found the same eight-byte value in the driver, its early `0x222044` response
and each inspected `clmods64.dll` image. A test-only replacement of both
observed copies in the second client did not change Gamma's remote close
after the 69-byte world follow-up. The `0x22201C` constant block was also
classified as twelve zero bytes and one nonzero word. Neither value is
confirmed as the server's duplicate-machine key.

The [solo trailer control](observations/2026-09-24-solo-trailer-control.md)
used the exact same altered `clmods64.dll` data value in a successful solo
Black entry and a failed concurrent Black entry. This gives an integrity
control for the **user-mode slot edit alone**: the edit can coexist with a
successful world session, but it does not isolate two clients on Gamma.
The same control was extended to the early full `0x222044` response trailer:
solo Black entered Gamma with the exact same trailer and user-mode copy
changes that still failed concurrently. See the focused note for both traces.

The [`0x22201C` session-word control](observations/2026-09-24-01c-session-word.md)
similarly matched the second client's early reply and its copied module DWORD.
Solo entry succeeded; concurrent Gamma entry still failed after 69 bytes.

The [world send-path observation](observations/2026-09-24-world-send-path.md)
traced the game's 37-byte send to a live `ws2_32!send` interception chain
that reaches `clmods64.dll`; the broker receives a 69-byte follow-up.
The lower Winsock hook confirmed that `clmods64.dll` sends those 69 bytes
in one call. Same-server dual entry is still not working.

The [world buffer and driver record analysis](observations/2026-09-24-world-buffer-layout.md)
now traces the 37-to-69 repackaging through `active64.sys`: the driver's
per-process record receives the output, an RC4-style state advances 67 bytes,
and the encrypted payload contains the original 35 request-body bytes plus
a new 32-byte field. The source of that field remains under investigation.
