# Registry callers during protected startup

The raw kernel ETW captures and parsers are local ignored files under
`workspace/two-client-isolation/` (`registry-stack.*`,
`registry-open-stack.*`, and `summarize-registry-callers.py`). The first capture
stack-walked `RegQueryValue` for a 25-second startup of original launcher PID
20652 and child `lu4.bin` PID 11404. A second capture stack-walked
`RegOpenKey` for launcher PID 21228 and child PID 15524. Both test process
trees were stopped; the existing Gamma client was not modified.

All four launcher `MachineGuid` reads ran through
`rsaenh -> cryptsp -> crypt32 -> nvldumdx -> d3d9`. All seven child
`MachineGuid` reads ran through `nvldumdx/d3d12core/d3d12` or directly through
`d3d12core/d3d12`. The six child `DevQuery\...\UUID` reads ran through
`cfgmgr32 -> MMDevAPI -> AudioSes -> XAudio2_9`. No captured stack for these
values contained `clmods64.dll`. These observed reads are graphics/crypto and
audio activity; the earlier inference that they were anticheat fingerprint
reads was unsupported. They cannot by themselves justify another early
`MachineGuid` or audio UUID spoof.

Of 133 child `RegQueryValue` events whose captured stacks contain
`clmods64.dll`, most came from dependent Windows UI, Winsock and IP Helper
initialization. One successful read is directly from `clmods64.dll` through
`ntdll.dll`: `ComputerName` at
`HKLM\SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName`.
The second ETW capture confirms that the corresponding key open also goes
directly through `clmods64.dll -> ntdll.dll`, followed by the query. The
current per-process agent replaces `ComputerName` on its late Win32 registry
hook path, but that does not demonstrate coverage of this earlier native call.

To test whether the existing root-only `GetProcAddress` import callback could
cover the native query without changing its result, an opt-in counter was
added. Test launcher PID 9288, child PID 1632, made 0x46 calls through that
resolver, including one `GetSystemFirmwareTable` resolution and two RSMB
calls, but **zero** resolutions returned the address of `NtQueryValueKey`.
The test returned every original address unchanged, built successfully and
the child was alive at the 25-second probe. Therefore the existing resolver
cannot be used as an established early `ComputerName` interception point.
The test processes were stopped; the ordinary release path is unaffected.

This identifies a specific gap, not the cause of Gamma's rejection. Neither
the server's decision nor the content/role of the early driver output is
visible in this ETW data. Do not globally patch `ntdll`, modify the anticheat
driver response, or mark same-world isolation complete based on this read.
