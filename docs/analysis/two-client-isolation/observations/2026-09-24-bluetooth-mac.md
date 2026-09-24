# Physical Bluetooth MAC copied into Active Anticheat state

## Read-only observation

On the live Gamma client, a scan of the loaded `active64.sys` image for the
machine's physical network-adapter MAC addresses found one exact six-byte
match for the **Bluetooth Network Connection** adapter at driver RVA
`0x12ABD56`. The same six bytes occurred once in the first Gamma client's
loaded `clmods64.dll` image, at RVA `0x7EAEA`. `uuid.getnode()` selected that
same Bluetooth address on this PC. The template MAC assigned to the Gamma
profile differs from it and had no exact six-byte match in the scanned
`clmods64.dll` image. The physical Ethernet MAC had no exact match in either
scanned image.

The driver match is adjacent to the previously observed session word at RVA
`0x12ABD4C` and eight-byte driver value at RVA `0x12ABD68`. This is concrete
evidence that an original hardware address survives into both anticheat
images despite the current template hooks. A six-byte match in two large
images is unlikely to be accidental, but the copy path and its role in the
69-byte world message are not yet established. No server rejection reason
has been observed. This alone does **not** prove the server uses that MAC to
reject a second Gamma connection.

## Next controlled test

Change only the cached six bytes in a disposable second Black process after
its prelogin gate, leaving the loaded driver, the first Gamma process and
persistent templates unchanged. Compare a solo Black entry with the same
edit against a paired Black entry. Read back the slot after the 69-byte
follow-up and confirm the first client's established world tunnel remains.
Do not use this experimental patch as a production isolation feature until
the paired entry succeeds and the mechanism is understood.

## Matched test result

The Black profile's fixed template MAC replaced only the six-byte
`clmods64.dll` copy at RVA `0x7EAEA` in a disposable Black process. Before
editing, the slot matched both the loaded driver's global at RVA
`0x12ABD56` and the host's Bluetooth MAC. Readback after the 69-byte world
follow-up confirmed that the template MAC remained in the child slot.

With the original Gamma client already in the world, Black PID 20772 sent
the 69-byte follow-up but did not enter Gamma. No next world reply was
observed; the first Gamma tunnel stayed established. The same edit then
allowed **solo** Black PID 16704 to enter Gamma and return a radar position
after the original client was stopped. The normal Gamma session was
restored as Collector PID 20964 / game PID 6576 with its proxy tunnel
established. Rediscover PIDs before later tests.

Thus changing this **one user-mode cached MAC** is insufficient to isolate
the second client. The test does not change the driver's global or other
derived values, and it does not rule out the MAC as one component of a
larger machine proof. The test-only patch code is in ignored `workspace/`;
no production launch behavior changed.

## Early driver-response path

An opt-in read-only scan of early Active Anticheat IOCTL replies found the
same physical Bluetooth MAC at offset `0x96` in the complete `0xB0`-byte
`0x22201C` reply. Three sampled `0x222044` replies had no exact six-byte
match. This identifies a concrete path by which the driver exposes the
physical MAC to the client module. It does not establish how the driver
initially learned the address.

A temporary root-only test branch called the real `0x22201C` first, copied
its original reply for diagnosis, and replaced six bytes at offset `0x96`
only when they exactly matched the host Bluetooth MAC. The branch was
enabled solely for disposable Black test processes. A first attempt had a
test-hook pointer error after `rep movsb`; its counter reported **zero**
replacements, so that attempt is excluded. After correcting the pointer,
the counter recorded **one** replacement. The second child's
`clmods64.dll` cached copy at RVA `0x7EAEA` then equalled the Black template
MAC before login and still equalled it after the 69-byte world request.

Paired Black PID 15356 still received upstream EOF after the 69-byte
request; the first Gamma tunnel stayed connected. A matched solo Black PID
15804, with the same early replacement and no direct cache edit, received
the next 193-byte world reply, entered Gamma and returned radar data.
Normal Gamma was restored as Collector PID 9668 / game PID 936 with an
established proxy tunnel. Rediscover all PIDs before later tests.

This proves that the **early reply carries the original MAC and feeds the
observed client cache**, and that replacing that path alone is insufficient
for concurrent Gamma entry. Other driver-global or derived anticheat state
remained unchanged. The temporary mutation branch should be removed from
native source after the control; the read-only match-offset diagnostic can
remain opt-in.

The temporary response-edit branch and its counter were removed after this
negative control. The final native and LiveSmoke win-x64 builds pass with
only opt-in, read-only match-offset logging. `git diff --check` passes. The
normal release package was not overwritten while Gamma was running.
