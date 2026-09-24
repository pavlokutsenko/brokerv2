# Controlled early ComputerName replacement

The preceding ETW capture showed an early direct
`clmods64.dll -> ntdll.dll` query of
`HKLM\SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName`.
The normal agent's later Win32 registry hook could not establish that this
early read used the launch template. To test whether the shared value explains
the two-client Gamma rejection, we temporarily set that one registry value
to the fixed Black template's `RegistryComputerName`. The value was verified
different from the physical machine name and valid as a Windows computer name.

The original value was saved in ignored local `workspace/` before each trial.
Each script restored it in `finally`; a separate hidden watchdog also checked
and restored it if still changed. Restoration was verified after both trials.
No other machine-wide registry value or anticheat driver data was changed.
The existing Gamma client PID 12416 remained in world as `LU4 - Grader`.

The second client ran through the saved Black profile, its fixed identity
template and authenticated HTTP proxy. The root-only early path confirmed one
full RSMB replacement and one adapter MAC replacement before the regular
agent took over. The first trial held the registry value for 24 seconds and
restored it before world selection. It again received HTTP CONNECT 200 for
login and world, then ended at the same 15-byte request, 13-byte reply,
69-byte follow-up and remote EOF. It cannot exclude a later name reread.

The second trial held the replacement for **55 seconds**, covering the world
exchange. ETW captured a successful `ComputerName` query directly from
`clmods64.dll` during this interval. The late client again reached radar and
the proxy returned HTTP 200 for both tunnels. At 00:07:23 UTC the world
exchange sent 15 bytes, received 13, sent 69, and the upstream closed at
00:07:25 UTC; no character list appeared. The value was restored after that
exchange. The test-owned Black PID 21088 exited and Gamma stayed alive.

This is evidence that **changing the early system ComputerName to the second
template is insufficient**, even when it remains changed through the server
decision and the already established early SMBIOS/MAC replacements are active.
The test does not prove what other field the server or anticheat uses. A
kernel registry callback solely for this field is therefore not justified by
the current evidence.

Raw ETL/text dumps, proxy timings and watchdog files remain ignored under
`workspace/two-client-isolation/` (`computername-20260924-030405.*` and
`computername-20260924-030647.*`). They should not be published because
the environment contains private machine and connection details.
