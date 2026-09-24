# `0x22201C` session-word control

The successful early `DeviceIoControl(0x22201C)` reply contains a 16-byte
block at offset `0x80`: twelve zero bytes followed by one DWORD. The full
block was not found in live images, but a **four-byte read-only scan** found
the DWORD exactly once in each inspected image: `active64.sys` RVA
`0x12ABD4C` and `clmods64.dll` RVA `0x7EFD8`. These RVAs apply only to the
currently inspected binaries. The field is 0x1C bytes before the previously
observed `0x222044` driver global. This is a copied shared value; its meaning
and presence in the network request are not known.

An FNV64 meet-in-the-middle decoder recovered the block's DWORD from early
trace fingerprints without writing the full response to disk. While the
original Gamma process stayed up, repeated second-client probes had the same
value `0x6EBF2792`. Later sessions showed `0x6EBF42C0`, `0x6EBF42E2`,
`0x6EBF44B7`, `0x6EBF44D3` and subsequent values. It was stable across
two prelogin probes in the same original-Gamma session, not globally fixed.
This corrects the narrower wording in the earlier driver-output note.

For a matched control, a temporary root-only hook called the real driver,
then replaced just the DWORD at offset `0x8C` in the **second client's**
complete 0xB0-byte reply with the previously observed `0x6EBF2792`. A
test-only prelogin gate let our own LU4Memory driver change the matching
`clmods64.dll` DWORD only in that test child; it checked that the old value
equaled the live driver global, wrote the new value and read it back. The
driver global and first process were not changed. The replacement still
matched in the second child after it sent the 69-byte world follow-up.

Solo Black PID 9656, using its saved fixed HWID template and authenticated
HTTP proxy, entered Gamma with this pair of changes. Its trace recorded one
early replacement, its 69-byte follow-up received 193 bytes, and the radar
reported a player position. The first Gamma client was then restored as
PID 1592 with an established proxy tunnel owned by Collector PID 19812.
Concurrent Black PID 5512 used **the same** replacement in both observed
copies and recorded one early replacement. It sent the 69-byte follow-up,
received zero further bytes, and its upstream proxy connection closed. The
first Gamma tunnel remained established. This pair of copies is therefore
insufficient to get a second client into the same world; the edit is
compatible with a solo successful entry.

One aborted paired attempt never reached the memory edit because the test
script cleared its protected first-PID environment variable while restoring
Gamma. The script was corrected and the paired test above actually completed.
An earlier solo attempt exited during Active Anticheat startup before its
prelogin gate; a repeat gate-only probe reached the gate, and the later solo
control succeeded. Neither failed setup attempt is evidence of a world
rejection. The temporary early-reply mutation branch was removed from the
native source after the controlled negative result. Raw logs and diagnostic
scripts remain under ignored `workspace/two-client-isolation/`.

The server continues to reject the second 69-byte world follow-up. The next
useful observation is which module builds that follow-up, and whether the
outbound message exposes a stable machine-wide field. This experiment does
not show that the DWORD itself is a machine identifier or a server check.
