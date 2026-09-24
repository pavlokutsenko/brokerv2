# Early registry reads and separate Windows account

The Windows Kernel Registry ETW capture is stored in ignored
`workspace/two-client-isolation/registry-probe.etl`. During normal startup,
the original launcher read and wrote
`HKCU\Software\ActiveAnticheat\1224067\ltpad` and queried
`HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`. The child also queried
`MachineGuid` and `HKLM\SYSTEM\CurrentControlSet\Control\DevQuery\{10,5,6}\UUID`.
The three DevQuery values are identical on this PC. ETW establishes the reads,
not their role in the server decision.

The opt-in root trace `PRICECHECK_TRACE_AA_REGISTRY=1` virtualized root
`MachineGuid` reads and `ltpad` without changing the actual registry values.
The root trace confirmed two successful `MachineGuid` replacements; a concurrent
Black launch still failed at the same Gamma world exchange. An early child
KernelBase IAT hook installed after child creation. A standalone launch once
counted five replacements, but a controlled concurrent launch counted 168
`NtQueryValueKey` calls and **zero** replacements. It must not be counted as
covering the child's pre-window `MachineGuid` in the failed world test.

`KernelBase.dll` was absent from a freshly created suspended `lu4.bin`; only
`ntdll.dll` was mapped at the checked system DLL bases. A test-only attempt to
load KernelBase with a remote thread before resuming the main thread installed
the IAT hook, but the child then stalled during Active Anticheat checks and its
early `0x222044` IOCTL failed. The experiment was removed from native source,
and the agent rebuilt. The first Gamma client was not touched.

A stronger user-profile control used a temporary local account, an elevated
batch logon token and the normal `lu4-win64-shipping.exe` launcher. With the
first Gamma client in the world, its separate-account child reached server
selection and timed out before character selection. After stopping the first
Gamma client, the same separate-account launch, account and scripted login
entered Gamma as character `Gerals`. This validates the alternate-account
launch and shows that a different Windows SID, HKCU and AppData alone do not
permit two same-world sessions. The temporary account is now disabled with a
fresh random password. No alternate-account game process remains.

The `PriceCheckLab` account and an old DPAPI-encrypted test credential may
remain. A previous automatic command review rejected their combined removal;
do not silently retry a differently packaged deletion. No credentials are
printed in the test trace.

A later path-alias test launched Black through a junction to the **whole**
installed game tree, so its executable path differed while the relative
`engine` and `lu4` directories remained available. It passed the game window,
radar and authenticated proxy checks, then failed at the same 15/13/69-byte
Gamma world exchange with the first client active. A junction pointing only
to `binaries\win64` failed earlier at Unreal project discovery and is not a
valid control. The full-tree junction was not tested alone, so this result
only shows that changing the launcher's path string did not solve the
concurrent case. Automatic command review rejected removal of the two
test-created junctions under ignored `workspace/`; their targets remain
untouched and the links are left in place.
