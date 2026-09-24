# Eight-byte `0x222044` trailer: driver-global controls

The earlier response and child-cache tests left the machine-global source
at active64 RVA `0x12ABD68` unchanged. A separate `LU4Probe` build now has
a test-only compare-and-swap IOCTL for **exactly eight bytes at that RVA**.
It requires the complete old value, PASSIVE_LEVEL, a valid loaded-image
range and readback. The ordinary `LU4Memory` build does not implement this
IOCTL. The normal Gamma/Collector were stopped **before** loading the
updated probe; Gamma was relaunched with an established proxy tunnel before
the controls. No probe-driver load occurred while either game window was
active during a world-entry comparison.

The replacement was the same eight-byte value recorded in the earlier
successful solo child-cache control. Two driver read paths agreed on the
original nonzero global and a no-op CAS succeeded. In paired Black PID
10064, the global was changed after anticheat startup and remained changed
at the 69-byte world send. The second connection received upstream EOF
without the next world reply; the first Gamma tunnel stayed connected.
Matched solo Black PID 13632 kept the same global edit at its world send
and entered Gamma. The original global was restored after each test and
normal Gamma returned as Collector PID 16796 / game PID 18396.

A second pair changed both the test child's `clmods64.dll` cache at RVA
`0x7EFA2` and the driver-global eight bytes to the same value. Paired Black
PID 13996 kept both replacements through its world send yet received
upstream EOF. Solo Black PID 6640 kept both replacements and entered Gamma.
The original global was restored, and normal Gamma returned as Collector
PID 20844 / game PID 5084. These controls rule out the global alone and
the global plus observed client cache as sufficient isolation fixes.

For the final matched control, a temporary opt-in root callback called the
real `0x222044` IOCTL, captured its original complete reply, then changed
only its final eight bytes in the test child's output. The initial test
harness tried to patch the client cache afterward and stopped **before
login** because the cache already matched the replacement. Its trace
showed three response substitutions. The harness was corrected to read
and accept an already matching cache; it did not blindly overwrite it.

Paired Black PID 19924 then showed the replacement in its cache **before**
the prelogin gate, four recorded response substitutions, and the matching
driver-global value still present at the 69-byte world send. It received
upstream EOF without the next reply, while first Gamma remained connected.
Solo Black PID 9936 with the **same early reply, cache and global values**
received the next world reply and entered Gamma. Both controls restored
the original driver-global bytes; normal Gamma returned as Collector PID
6736 / game PID 14020 with an established tunnel. The temporary early
response mutation branch was then removed from native source.

This excludes the three observed copies of the eight-byte trailer as a
sufficient two-client fix. It also corrects the earlier narrower finding
that changing this early reply did not update the cache: in this later
callback and build, the changed trailer was already visible in the cache
at the prelogin gate. The difference may be callback placement or repeated
`0x222044` calls; the root traces recorded four substitutions in both
the valid paired and solo runs. Neither result proves what the eight
bytes mean or why the server rejects the second same-world connection.

After the test, the original eight-byte, adjacent six-byte and MAC globals
were each verified through independent driver readers. The separate
`LU4Probe` service was stopped and deleted; ordinary `LU4Memory` stayed in
place. Collector PID 6736 and Gamma PID 14020 remained alive with an
established world proxy tunnel. The root `build.ps1` staging build, ordinary
driver build, LiveSmoke win-x64 build, staged native-DLL hash comparison and
`git diff --check` all passed after the temporary response mutation was
removed. The active release Collector was not replaced while its game was
running.
