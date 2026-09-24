# World follow-up send path

The normal solo/concurrent control was repeated with packet capture enabled
only in the test broker. Solo Black PID 16244 entered Gamma; the original
Gamma was restored as PID 11908 under Collector PID 11472 with an established
50100 proxy tunnel. Concurrent Black PID 18396 failed at the usual point
after its world follow-up, while the first tunnel stayed established. Both
used the same saved profile, fixed template and authenticated HTTP proxy.
Their broker-visible 69-byte follow-ups had the same first two bytes and
different values in the other 67 positions. This comparison does not expose
a plaintext machine identifier or explain the remote close. Raw packets are
in ignored LocalAppData test logs; no packet bytes are included here.

A test-only hook on `send`/`WSASend` in the second `lu4.bin` process recorded
the local calls that precede the broker's 69-byte read: two 34-byte login
sends, then a 15-byte world send and a **37-byte world send**. The 37-byte
send occurred at the same time as the broker's 69-byte follow-up. The hook
recorded a stack through `PriceCheck.ClientAgent.dll` to the return address
`lu4.bin` RVA `0x16CB803`, then game RVAs `0x4C22A0B` and `0x975EB95`.
Read-only live code at `0x16CB7FE` calls a thunk at `0x9A4313E`, returning
at `0x16CB803`. The on-disk `lu4.bin` bytes at this offset are not the live
code; use a live read for further disassembly.

The test hook hashed the 37 source bytes with FNV32. Its hash did not match
any contiguous 37-byte window in the broker-visible 69-byte follow-up from
the same test PID 10524. Thus the broker did not receive an unmodified copy
of those 37 bytes as a simple prefix, suffix or insertion. The 32-byte
difference between call size and broker read size alone could still have
been TCP coalescing.

An exact byte comparison in paired Black PID 16572 reconstructed the 37
source bytes from a temporary opt-in test trace and compared them with the
same run's 69 bytes captured at the broker. No 32-byte insertion position
reproduced the broker message, and the longest shared contiguous run was
only one byte. The temporary raw source-byte logging was removed after this
comparison; source bytes were never added to tracked files. This shows the
37-byte input is substantially transformed, without identifying the cipher
or the machine inputs.

Read-only inspection of first Gamma PID 11908 found that `ws2_32!send` starts
with a jump to a private executable region. Its branch checks a private
state word and dispatches either to `ws2_32!send+5` or to `clmods64.dll`
RVA `0xB700`. This establishes an Active Anticheat send interception path in
the live client. The `clmods64.dll` target's bytes look like transformed
runtime code and cannot be understood by straight-line disassembly of the
current memory snapshot. The finding does not yet prove that this hook
creates all 69 bytes; the next probe resolves that point. It still does not
show what data the server validates.

To resolve the transport ambiguity, a second test-only hook was placed at
`ws2_32!send+5`, after the Active Anticheat branch. In Black PID 8268 the
upper hook logged 37 bytes and, two milliseconds later, the lower hook
logged **one 69-byte send on the world socket**. There was no separate
32-byte `send` through this lower function. The lower stack's first module
frame was `clmods64.dll` RVA `0x1633E99`, followed by the same game return
address `0x16CB803`. This confirms that the `clmods64.dll` interception
path expands or transforms the game's 37-byte world follow-up into a single
69-byte Winsock send. Its contents and per-machine inputs remain unknown.
The first attempt at this lower hook failed in the launcher because the
anticheat detour had not been installed there; the opt-in installer now skips
that lower hook until it sees the detour in the game child.

A read-only 12-second pause during the lower callback in test child PID
19548 allowed a live read around `clmods64.dll` RVA `0x1633E99` while the
function was on the stack. Its bytes matched those in first Gamma PID 11908,
but straight-line decoding did not find a valid call ending at the return
address. This runtime region is not yet suitable for simple disassembly.
Only the test child was paused and then stopped; first Gamma stayed connected.

Instrumentation is gated by `PRICECHECK_TEST_WORLD_SEND_STACK=1` in the
native agent and `PRICECHECK_TEST_CAPTURE_WORLD_PREFIX=1` in the broker.
The native hook logs lengths, a 32-bit source fingerprint and stack module
RVAs; the packet capture writes only to ignored LocalAppData logs when the
flag is explicitly set. The native and LiveSmoke win-x64 builds pass and
their agent DLL hashes match. A first stack test mistakenly ran an older
staged agent; subsequent tests checked the test output hash before launch.

Next: trace the inputs to the `clmods64.dll` transformation and determine
whether the 69-byte world request contains a per-machine proof. A successful
second-PC trace would help distinguish server checks from local shared driver
state. Same-world dual entry remains unverified.
