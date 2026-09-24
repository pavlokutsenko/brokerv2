# Controlled edit of the active64 machine-global MAC copy

The physical Bluetooth MAC was present at loaded `active64.sys` RVA
`0x12ABD56` and in the early `0x22201C` reply at offset `0x96`. The prior
test changed only the early reply and client cache; a second Gamma entry
still failed. This control asked whether the shared driver copy was the
upstream source or an independent part of the world proof.

## Guarded test path

A separate `LU4Probe` build of our `LU4Memory` driver was loaded alongside
the normal driver. It has its own device name, does not register WFP proxy
callouts, and accepts one test-only compare-and-swap IOCTL restricted to six
bytes at active64 RVA `0x12ABD56`. The request must name the exact old bytes;
the handler verifies a readback. The ordinary `LU4Memory` build does not
implement this IOCTL. Both drivers' read-only APIs agreed on the current
six bytes, which matched the host Bluetooth adapter. A no-op write of the
same bytes succeeded before any actual substitution. The test copied the
original bytes to an ignored workspace recovery record before editing.

## Paired attempt and attribution correction

With normal Gamma initially active, the separate probe driver was loaded,
then the shared MAC bytes were edited. The Black harness found the first
Gamma game process already absent and therefore did not start a second
client. The original six bytes were restored immediately. A later normal
Gamma process also disappeared after the probe driver was loaded again,
**without another MAC edit**. The user independently observed that loading
a driver closes the game windows. Therefore this attempt cannot attribute
the first process exit to the MAC edit. Loading the probe before launching
either game is required for a valid paired control.

## Solo control

After deliberately stopping normal Gamma, the test changed the global MAC
to the Black template before launching a solo Black client. Black PID
20232 entered Gamma and returned a radar position.
At its prelogin gate, however, `clmods64.dll` RVA `0x7EAEA` still held the
physical MAC, not the template MAC. A second solo run, Black PID 13916,
repeated this observation and performed a full read-only scan of the loaded
active64 image while the test had not yet restored its saved value. The
physical MAC again occurred exactly once, **at the same driver RVA
`0x12ABD56`**. Thus a new client launch repopulates this global slot from
the physical source, undoing the prelaunch edit. The client cache can still
be a copy of this slot after it refreshes; the earlier inference of an
independent response source was too strong and is withdrawn.

Both solo runs entered Gamma with the original MAC visible by prelogin, so
neither tests a changed driver MAC at the world boundary. The test restored
and verified the original value, then normal Gamma returned as Collector
PID 13520 / game PID 7552 with an established proxy tunnel. Rediscover
PIDs before later tests.

The test leaves no production behavior that mutates active64. The next
valid causal control is to load the separate probe **before** starting both
games, let the second client complete anticheat initialization, then edit
the refreshed slot at its prelogin gate. Verify that the replacement stays
through the 69-byte world request and that the first Gamma tunnel survives.
No server-side rejection reason is available yet.

## Valid paired control after driver loading

With `LU4Probe` already loaded before the first Gamma client started, the
first world tunnel was established. The disposable Black child initialized
the anticheat and paused before login. The test changed its cached
`clmods64.dll` MAC and then compare-and-swapped the refreshed active64
global MAC to the Black template value. At the 69-byte world request, both
copies still matched the template. Black PID 20756 did **not** enter Gamma:
the upstream world connection closed before the next 193-byte reply. The
first Gamma PID 7552 remained alive with its proxy tunnel established.
The global was restored and verified immediately after the test.

This is a valid negative control for **these two MAC copies together**.
It does not cover other active64 state, an input captured before the
prelogin gate, or the 32-byte field's other potential inputs. The global
edit itself did not close the first Gamma client in this controlled run.
