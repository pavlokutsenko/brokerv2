# Early `0x22201C` reply layout and six-byte control

The root-only, opt-in early IOCTL observer saved the complete 176-byte
`0x22201C` reply from two Black launches while ordinary Gamma was in world.
It captured before login and before changing the response. The local raw
files are ignored under `workspace/two-client-isolation/`; they are not
committed because they may contain machine and session material. A harness
bug initially searched for the dotnet probe PID; the writer actually names
the file with the native root PID. The harness was corrected, and both
paired captures were verified to contain exactly 176 bytes.

The paired replies matched at 29 of 176 byte positions. Their offset
`0x80–0x9B` was identical: twelve zero bytes, a four-byte session word at
`0x8C`, six further bytes at `0x90`, and the physical Bluetooth MAC at
`0x96`. A read-only search found the six bytes at `active64.sys` loaded RVA
`0x12ABD50`, exactly between the known session word at RVA `0x12ABD4C` and
physical MAC at RVA `0x12ABD56`. A read-only scan of the first Gamma
`clmods64.dll` mapped image found no exact copy of these six bytes. The
paired replies' first 128 bytes differed at all but one byte; their tail
from `0x9C` also varied. The unchanging six bytes are a shared driver
value, **not yet identified as a hardware identifier or server field**.

For a successful control, ordinary Gamma was stopped and an unmodified
solo Black client entered Gamma and returned radar data. Its early reply
kept the same six bytes at `0x90` and physical MAC at `0x96`. The session
word at `0x8C` differed, while the varying first 128 bytes remained
different. Ordinary Collector/Gamma was restored with an established proxy
tunnel. Thus the two shared six-byte fields are present in both a successful
solo and failed concurrent setup. This is compatible with a server-side
unique-machine rule, but does not prove one.

An opt-in, temporary test branch then changed only the six reply bytes at
`0x90`, after the real successful IOCTL. The write was guarded by all six
expected original bytes and used one fixed replacement for both runs. The
test counter confirmed exactly one substitution in each child. Solo Black
PID 9364 entered Gamma and received the next world reply. After normal
Gamma was restored as PID 12800 / Collector PID 20324, concurrent Black
PID 20704 with the **same substitution** sent the 69-byte world follow-up
but received no next reply; the upstream connection closed and the first
Gamma tunnel remained established. The changed early six bytes alone are
therefore tolerated in solo play but insufficient for two Gamma entries.
The temporary response-mutation branch was removed from native source.

No driver was loaded during these paired/solo trials. `LU4Probe` was
already present before the first Gamma process launched; this avoids the
known confound that loading a driver closes game windows. The normal
Collector was restored after each solo control.

## Driver-global control

The separate test-only `LU4Probe` compare-and-swap handler was extended to
allow the adjacent six-byte slot at active64 RVA `0x12ABD50`, in addition
to its original MAC slot. It still accepts only those two exact offsets,
requires all six old bytes to match, performs a readback and exposes this
write path only in the separate probe build. The ordinary `LU4Memory`
driver does not implement the test IOCTL. The first Gamma window and
Collector were deliberately stopped before the probe driver was rebuilt
and loaded, then Gamma was relaunched and its proxy tunnel established.
Both driver readers agreed on the original six bytes; a no-op CAS passed.

In the paired control, Black PID 7164 reached the prelogin gate after its
anticheat startup. The probe replaced the six driver-global bytes with the
same fixed value used for the earlier response-only test. Readback at the
69-byte world request confirmed the replacement was still present. Black
again received no next world reply and the upstream socket closed. The
first Gamma PID 21228 and its world tunnel remained active. The original
six bytes were restored and verified.

In the matched solo control, the first Gamma was stopped. Black PID 10400
used the **same global replacement**, which persisted through its 69-byte
world request, and entered Gamma with radar data. The original bytes were
restored before ordinary Collector/Gamma was relaunched as PID 1052 /
PID 14372 with an established proxy tunnel. Read-only checks confirmed
both the original six-byte slot and original MAC slot afterward.

This rules out the **driver-global six-byte slot alone** as a sufficient
concurrent-entry fix and shows the edit is compatible with a successful
solo entry. It does not cover a matched edit of both the early reply and
global slot, or other shared anticheat state. The original `0x22201C`
reply was captured before the late global edit, so the client saw the
original six bytes in this control. Same-world dual entry remains
unverified.

## Combined response and global control

The same guarded response substitution was temporarily restored for a
combined control. Concurrent Black PID 5140 recorded exactly one early
`0x22201C` six-byte substitution. The matching driver-global bytes were
replaced at the child's prelogin gate and verified still changed at its
69-byte world request. The first Gamma tunnel stayed established, but Black
again received no next reply and upstream EOF. Solo Black PID 18988 used
the **same two substitutions**, received the next world reply and entered
Gamma. Both tests restored the original driver-global bytes immediately;
normal Collector/Gamma was restored as PID 20968 / PID 15780 with an
established tunnel. The temporary early-response mutation branch was
removed again after this matched negative result.

The early six-byte copy plus refreshed driver-global six-byte slot are
insufficient together for a second Gamma world entry. This still does not
rule out other values in the 176-byte response, the larger `0x222044`
response, per-process driver records, or server-side policy outside these
IOCTL paths.
