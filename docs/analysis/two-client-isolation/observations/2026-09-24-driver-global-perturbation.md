# Driver global and controlled child-only changes

Read-only `LU4Memory` inspection found a nonzero eight-byte runtime value at
`active64.sys` RVA `0x12ABD68`. Its SHA-256 prefix is
`ba5f276b9f1bd915`. The value exactly matches the trailing eight bytes of
four previously captured full `DeviceIoControl(0x222044)` responses from
failed concurrent Black launches. A read-only scan found the same eight
bytes once in each inspected `clmods64.dll` process image, at RVA `0x7EFA2`.
This establishes that a machine-wide driver value reaches user-mode anticheat
memory. It does **not** establish its meaning or whether the world server
uses it to reject the second client.

For a causal test, the root-only early IAT callback gained an opt-in
`PRICECHECK_EARLY_044_REPLACE_HEX` branch. It calls the original driver
first, and only when a successful `0x222044` response returned the complete
`0x4BA` bytes does it replace the final eight bytes in the **test child's
output buffer**. It never changes the driver global, the first client's
memory or ordinary launches. The root trace recorded one successful
replacement in the test run. Before login, the child's `clmods64.dll` slot
still held the original driver global, so the response edit alone did not
establish a separate cached value. This temporary mutation branch was removed
from source after the test; only the opt-in read-only capture remains.

The next test set that second child's `clmods64.dll` slot at RVA `0x7EFA2` to
the same random eight bytes used in the early response. A test-only prelogin
gate allowed a readback before login. The replacement remained in that slot
through the 69-byte world follow-up. Black used its fixed launch template,
the early SMBIOS and adapter replacements were active, and its authenticated
proxy returned HTTP CONNECT 200 for both login and world. Gamma still closed
the world stream after the 15-byte request, 13-byte reply and 69-byte
follow-up; no character list appeared. The original Gamma `LU4 - Grader`
process stayed alive. Changing these two observed copies of the shared value
is therefore **insufficient**. Other driver state, copies and the server's
decision are unobserved, so this result cannot exclude the value from a
larger proof.

The unchanged 16-byte block eight of the early `0x22201C` response was also
captured in a stopped-at-prelogin test child. Twelve bytes are zero; only its
last 32-bit word is nonzero (`0x6EBF2792` in this boot). The complete block
was absent from read-only scans of both inspected live `clmods64.dll` images
and the loaded `active64.sys` image. That 32-bit word was absent verbatim
from the driver file. Its semantics remain unknown. No change to this block
has been made.

Later read-only four-byte scans found that block's DWORD alone in the driver
and user-mode module, and captures from later sessions showed it changes.
The matched solo/concurrent substitution result is in
[`2026-09-24-01c-session-word.md`](2026-09-24-01c-session-word.md). The
word was fixed only across the earlier observations with one Gamma session
alive; the full 16-byte pattern being absent from image scans remains true.

Static follow-up on the driver's direct `MmGetPhysicalAddress` /
`MmMapIoSpace` imports found a helper at `0x140109C20` that copies one 4 KiB
page. Its caller at `0x140109CC0` iterates arbitrary address ranges, checks
IRQL and address limits, and can use a separate read callback. It has thirteen
direct references. This is a generic kernel-memory read path; the mapped
page alone is **not** evidence of SMBIOS or a hardware-identity read. No
direct link from this helper to the world-entry IOCTL cases was established.

The live `clmods64.dll` image contains static `CurrentVersion` and
`InstallDate` strings, but a read-only RIP-relative reference scan across its
three executable sections found no direct references to either string or to
its `RegOpenKeyExW`/`RegEnumValueW` import slots. The module uses transformed
runtime code, so the absence of simple references does not prove those
registry paths are unused. It does not justify adding an early InstallDate
hook without a call trace.

The root `build.ps1 -SkipBroker -OutputDirectory workspace\build-verify`
passed. Native `ClientLaunch/build.ps1` and the `ClientLaunch.LiveSmoke`
Release build also passed after removing the temporary mutation branch. The
root staging build was repeated, and its staged agent/login DLL hashes match
the latest native build. The normal Collector and first Gamma client still
use the previously published DLLs. Raw traces,
replacement bytes and prelogin scripts remain in ignored
`workspace/two-client-isolation/`.

Later static follow-up found three more runtime-filled arrays adjacent to the
known eight-byte global: 20 bytes at RVA `0x12ABD90`, 20 bytes at
`0x12ABDA4`, and 38 bytes at `0x12ABDB8`. All corresponding on-disk bytes
are zero. Driver function `0x14002BD70`, called from the per-process crypto
context initializer, hashes the 38-byte array to 20 bytes, initializes an
RC4-style state from one 20-byte array, transforms the hash, and compares it
with the other 20-byte array. The three complete live arrays were absent
from a read-only scan of the first client's `clmods64.dll` image. This is
consistent with an internal integrity or key check; it is **not** evidence
that any array is a machine identifier. No array or driver global was
modified in this follow-up.
