# World send buffer layout

The opt-in `PRICECHECK_TEST_WORLD_BUFFER_LAYOUT=1` probe retained only the
pointer to the game's 37-byte `send` buffer for the duration of that call.
When the Active Anticheat hook reached the lower Winsock entry with 69 bytes,
it compared addresses and checked whether each address was on the current
thread's stack. It logged no pointer values or packet contents. The pointer
is cleared when the upper call returns.

In a paired Black launch (PID 9808), the normal Gamma client (PID 18016,
Collector PID 11940) remained in the world with an established authenticated
proxy tunnel. Black again timed out before character selection. The lower
hook observed a single 69-byte world send; the upper hook observed the
37-byte game send. The two buffers had different addresses, more than 1 MiB
apart, and neither was on the thread stack. The five observed Active
Anticheat IOCTL calls retained their previous 164/59/40/59/59-byte pattern.
The first Gamma process and tunnel survived the failed test.

This rules out in-place expansion of the original 37-byte buffer in this
observed path. The second buffer is built elsewhere before the lower send;
its allocator, contents and machine-derived inputs remain unknown. The
address check does not prove whether the second allocation is on a heap or
in another memory region. Two simultaneous Gamma world entries remain
unverified.

Follow-up opt-in `VirtualQuery` tracing in paired Black PIDs 9364 and 19564
classified the source as `MEM_PRIVATE` and the wire buffer as `MEM_IMAGE`.
The latter was in the `.data` section of `clmods64.dll` at RVA `0x7F01D` in
both runs. A read-only scan of the first client's decrypted executable
pages found no simple RIP-relative reference in the nearby `0x7EF00`–
`0x7F100` data range; this does not exclude indirect or obfuscated access.

Hash-only checkpoints in paired Black PIDs 8268 and 5196 showed that the
69-byte slot changes between entering and returning from the second
`DeviceIoControl(0x222160)`, after the 13-byte server challenge. The hash
immediately after this call exactly matched the lower Winsock send's
69-byte buffer in each run. The first `0x222160`, `0x222158` and `0x22215C`
did not change that slot at their call boundaries. The slot also acquired
an earlier stable state between the first `0x222158` and first `0x222160`,
outside either instrumented call.

The result repeated in paired PID 3956. A classifier found **no direct
eight-byte pointer into the `clmods64.dll` image** at any alignment in the
observed 164-, 40- or 59-byte inputs (PID 20288); this includes no direct
pointer to the 69-byte slot. It does not rule out an encoded pointer or a
pointer registered through a different request. In PID 1616, the second
`0x222160` changed 68 of 69 slot bytes, from offset 0 through offset 68,
across all nine eight-byte blocks. The first and third `0x222160` changed
zero slot bytes. This is evidence of a near-complete transformation at that
call boundary, rather than a small unchanged token insertion. It does not
prove whether the kernel handler itself writes those bytes or another thread
does so during the synchronous call.

A matched **successful solo** Black control then stopped the first Gamma,
entered Gamma as Black PID 15576 with the same saved account, proxy and fixed
template, and restored normal Gamma under Collector PID 10440 / child PID
19736 with an established proxy tunnel. The successful world request used
the same `clmods64.dll` slot RVA `0x7F01D`; the second `0x222160` again
changed 68 of 69 bytes across all blocks and the post-call hash matched the
sent packet. Thus this buffer transformation is part of the normal world
protocol in both the successful solo and failed concurrent cases. Later
IOCTLs during the successful world session also updated the same slot, so
the slot is reused; the initial 69-byte send is the comparison target.

An offline scan of three earlier 59-byte near-send input captures (one
successful solo, two failed concurrent) found no unaligned eight-byte value
in the ordinary user-mode pointer range. Together with the live direct
pointer check, this makes a plaintext pointer in that particular input
unlikely. An encoded pointer, prior registration, kernel-side module lookup
or other mechanism remains possible. The active anticheat driver imports
`KeStackAttachProcess`, but its use in this IOCTL path has not been proven.

An opt-in `NtDeviceIoControlFile` boundary hook was then installed only in a
paired test child (PID 15092). It reached the normal 69-byte world send and
failed at character selection while the restored first Gamma remained
connected. For the second `0x222160`, the slot hash differed between entry
to and return from the **ntdll syscall boundary**; the return hash matched
both the outer `DeviceIoControl` return and the sent 69-byte buffer. The
first and third `0x222160` left the slot unchanged at both boundaries.
This excludes a post-syscall transformation in the observed KernelBase
wrapper. A driver-side write is the leading explanation, though an exactly
concurrent writer during the synchronous call has not been ruled out by
this read-only timing probe. The `active64.sys` IOCTL handler's machine
inputs and the server's rejection decision are still unknown.

Prior independent HTTPS tests through the two saved proxy credentials
reported different external IPs. The user also reported that both accounts
can enter Gamma from separate physical PCs on the same network. Thus neither
a shared public address nor a simple proxy-route collision explains the
current failure. These observations do not identify the server's rejection
rule.

## Kernel record copy (2026-09-24)

Static inspection of `active64.sys` located the `0x222160` dispatch case and
its per-process record lookup. The lookup matches the requestor's EPROCESS
pointer against a table of up to 50 records, each `0x54480` bytes. The case
locks the matched record and calls the driver's internal world helper. This
establishes that the observed request is handled with per-process driver
state, though it does not establish all inputs used to create the reply.

An opt-in 1.5-second pause at the second `NtDeviceIoControlFile(0x222160)`
boundary allowed read-only snapshots of the test-owned Black driver's record.
The record grew from 1,571 to 1,635 nonzero bytes during that transition;
42 sixteen-byte blocks changed, concentrated around record offsets
`0x4060`–`0x40A0`, `0x240C0`–`0x242D0` and `0x245F0`–`0x24610`. A separate
read-only comparison found an **exact 69-byte match** between the client's
`clmods64.dll` send slot (RVA `0x7F01D`) and driver record offset `0x4068`.
The same exact match was observed in the concurrently running, successful
Gamma client. Comparisons reported offsets and match lengths only; packet
contents were not written into the tracked notes.

The driver therefore retains a per-process copy of the 69-byte world send at
record `+0x4068`. The evidence does not yet prove whether the driver
generates the bytes at that offset or receives them from user mode through
an earlier registration. The second client's remote close after sending the
packet remains unexplained, and dual Gamma entry is still unverified.

Static call-target decoding then found three relevant operations in the
obfuscated world helper. One site passes record `+0x4068` as the destination
to a wrapper that probes a user buffer for reading and copies into the
record (`0x14005FAB2` -> `0x140109B20`). A second site passes record
`+0x4068` as the source to a wrapper that probes a user destination for
writing and copies out (`0x1400629C0` -> `0x140109BA0`). A third site passes
record `+0x240CC + index*0x7B8` as a cipher state and record `+0x4068` as
both cipher input and output (`0x140063BEF` -> `0x14010A1A0`). The latter
routine is an RC4-style bytewise stream transform. These sites show that
the driver has an in-record encryption path and user-mode copy paths. The
flattened control flow has not yet established which sites execute in the
observed `0x222160` call or their order. A controlled pre/post state capture
is needed before attributing the server decision to this transform.

Four paired pre/post captures around the second `NtDeviceIoControlFile` then
checked that control flow without printing packet bytes. The valid RC4-style
state at record `+0x240CC` advanced by **exactly 67 bytes** in four measured
runs; its index changed from 0 to 67. The state cannot be explained by
transforming the old record `+0x4068` buffer directly. The user-mode
`clmods64.dll` slot and the record's `+0x4068` buffer were identical after
the call. Before the call, the record buffer had only seven nonzero bytes,
while the user-mode slot had 36.

Using the pre-call state to reverse 67 bytes of the resulting 69-byte
user-mode slot showed the relevant cipher starts at offset **2**. The first
35 recovered bytes exactly matched user-mode slot offsets 2–36 before the
call, and the pre-call slot's first 37 bytes matched the game's original
37-byte `send` fingerprint. The remaining **32** recovered bytes did not
match the pre-call slot (one incidental equal byte in one run); the first
two on-wire bytes also changed. This is direct evidence that the driver
repackages the 37-byte game request into a 69-byte request: a two-byte
header, 35 original payload bytes and a 32-byte added field are then
encrypted over the last 67 bytes. The field's meaning and the server's
duplicate-machine rule are still unknown. The same-world test still fails
and the first Gamma client's tunnel survives.

Two further paired Black runs with the same account, template and proxy
produced different SHA-256 hashes of the recovered 32-byte field. It is
therefore **not a fixed 32-byte plaintext machine identifier** in these
observations. It may depend on the server challenge, session state or a
machine-derived secret; the comparison does not distinguish those sources.
The diagnostic record selector initially assumed the second client would
always occupy slot 1. One test used slot 2 and produced no valid capture;
the selector now finds the minimally populated active pre-world record and
reuses that slot at the post-call checkpoint. This failed capture is excluded
from the two valid hash comparison.

A two-run comparison of the pre-call original 37-byte game request for the
same Black account and template found only positions 0, 1 and 18 equal.
Positions 0–1 are the fixed request header; position 18 may be incidental.
There is no evident contiguous fixed machine identifier in this original
request across those two sessions. This does not exclude an encoded or
challenge-dependent machine value, nor a different server-side decision
input. The temporary raw 37-byte comparison file was deleted after the
comparison; only equal positions are recorded here.

Static call inventories further narrowed the 32-byte builder. The flattened
`0x140095620` helper calls local byte-mixing routines, RC4 and record/status
helpers; the related `0x14002C780` context initializer calls RC4 key setup,
copy/address checks and local obfuscated helpers. Neither examined function
has a direct imported firmware, disk, network or registry query in its
decoded call list. The initializer stores the current kernel thread pointer
at context `+0x10` (`record + 0x240B0`). This supports a per-connection
cryptographic context but does **not** rule out hardware data supplied through
an earlier request or an indirect helper. No candidate HWID field is yet
verified, so modifying the 32 bytes or driver state would be speculative.

Finally, a matched **successful solo** Black control repeated the same
read-only boundary capture with Gamma temporarily stopped. Black PID 7988
entered Gamma and returned a radar position. Its `0x222160` state also
advanced exactly 67 bytes; reversing bytes 2–68 recovered the first 35
original body bytes followed by a new 32-byte field, and the record's
69-byte buffer equalled the user-mode send slot after the call. Thus the
observed packet layout and transform are normal in both success and
failure. The distinction is not a missing `0x222160` call, missing 32-byte
field or different packet length. Ordinary Gamma was restored under
Collector PID 1864 / game PID 13624 with an established proxy tunnel.

The `0x14002C780` context initializer has 17 indirect calls. Decoding their
obfuscated targets identifies RC4 key setup, RC4 byte transformation,
user-address validation, copying, status updates and two local helpers.
The 32-byte builder `0x140095620` similarly has local cryptographic and
status targets. Neither function directly references the known driver-global
eight-byte value at RVA `0x12ABD68` or its adjacent runtime arrays. One
initializer subhelper (`0x14002BD70`) does reference three adjacent arrays
and performs a 20-byte hash/RC4 comparison; its role is not yet established.
This narrows where to look for a machine-derived input but does not prove
the server's duplicate criterion.
