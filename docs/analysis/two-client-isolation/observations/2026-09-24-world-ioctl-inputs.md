# Anticheat IOCTL inputs at Gamma world entry

The existing `DeviceIoControl` test hook was extended behind opt-in flags to
hash, without changing, the inputs to Active Anticheat IOCTLs `0x222158`,
`0x22215C` and `0x222160`. A paired Black trial (PID 20568) observed the same
five-call sequence already documented in `../README.md`: 164-byte `0x222158`
with a four-byte output, 59-byte `0x222160`, 40-byte `0x22215C` after the
13-byte world reply, 59-byte `0x222160` within a millisecond of the 69-byte
world send, and another 59-byte `0x222160` at the disconnect. All calls
returned success. Every sampled input had **zero changed bytes** when read
again immediately after its IOCTL. The 59-byte buffer is therefore not
expanded in place into the 69-byte message by `DeviceIoControl` in this
observed path. This does not exclude driver-side state changes or a separate
user-mode transformation.

Opt-in FNV32 fingerprints of all possible contiguous windows gave no exact
match between the game's 37-byte send and any 37-byte window of the driver's
59-byte inputs (three calls, 23 windows each), or between a full 59-byte
driver input and any 59-byte window of the 69-byte wire send (11 windows).
The 40-byte `0x22215C` input matched none of the 30 possible 40-byte wire
windows. An independent one-time raw comparison found no contiguous shared
run longer than one byte for the 164-, 40- or near-send 59-byte driver inputs
against the 69-byte wire send in a failed paired run (PID 20180). These
controls rule out simple unchanged insertion of those complete buffers; they
do not identify the transform or prove encryption.

A matched control used the same saved Black account, fixed hardware template
and authenticated proxy. With normal Gamma stopped, Black PID 2176 reached
Gamma and produced a radar snapshot. The ordinary Gamma client was restored
with an established proxy tunnel; Black PID 16504 then failed after the
69-byte world request while the first tunnel stayed connected. The two
69-byte requests differed at 67 positions. Temporarily captured input
buffers from those runs and an earlier failed paired run were compared by
position. For the first four corresponding calls, the two failed runs shared
zero byte positions in the 164-byte input, one of 59 in the first `0x222160`,
zero of 40 in `0x22215C`, and zero of 59 in the near-send `0x222160`.
The successful solo buffers likewise had zero or one matching positions
against each failed run. No saved Black hardware-template value appeared
verbatim in ASCII, UTF-16 or packed hex in those inputs. The buffers vary
strongly even under the same condition, so this byte comparison does not
isolate a server-visible machine field.

The one-time raw input writer was removed after analysis. Raw IOCTL samples
and world prefixes remain only in ignored LocalAppData test logs; tracked
notes include no payload bytes, credentials or session tokens. The opt-in
hash-only code remains for further no-content correlation. Native and
LiveSmoke builds passed after each test; the normal Collector/Gamma was
restored and the paired test-owned client terminated. Two simultaneous Gamma
world entries remain unverified. A server-side rejection reason, or
devirtualization/dataflow tracing of the `clmods64.dll` send transform, is
needed before making a justified production isolation change.

A final opt-in check read the four-byte output of `0x222158` without changing
it. One successful solo Black launch and two failed paired Black launches
all returned different nontrivial 32-bit values. It is not a stable literal
success/failure flag for the tested conditions. It may be a session value or
encode other state; its meaning remains unproven. The temporary output-value
trace branch was removed after this control. All three launches used the
same saved Black account/template, and the first Gamma tunnel survived both
paired failures.
