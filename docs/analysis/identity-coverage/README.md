# Identity surface coverage review (2026-09-29)

Scope: desk research and source inspection only. No client run, new hook, host
setting, or release change was made. This note is a coverage map, not evidence
that LU4 queries any candidate below or accepts substituted results.

## Verified local baseline

- The published USB/HID implementation maps selected SetupAPI and Configuration
  Manager device *instance IDs* and maps those IDs back when reopening a
  devnode. It does not map device *interface paths*.
- WMI substitution currently targets selected properties returned via one
  `IWbemClassObject::Get` implementation. It is not a WMI provider or a general
  interception of every WMI/MI client path.
- Identity hooks cover selected `RegQueryValueEx`, `GetAdaptersAddresses`,
  `GetAdaptersInfo`, `GetVolumeInformation`, `SendARP`, `DeviceIoControl`, and
  `GetSystemFirmwareTable('RSMB')` results. The separate registry trace can
  observe `RegGetValue`, but observation is not substitution.
- Live evidence supports adapter-address substitution after agent installation.
  An experimental early load observed two raw SMBIOS reads before the normal
  agent is installed. It did not yield a viable production loader.
- `CPUID`, kernel-origin reads, real PnP state, and remote account decisions
  are not changed by the current process DLL or memory driver.

## Candidate gaps, ordered for investigation

| Priority | Surface | Why current coverage may differ | Decision gate |
| --- | --- | --- | --- |
| 1 | Timing of reads | Normal DLL arrives after the game window; earlier results may already be cached. | Observe startup phases and call timing without recording raw identifiers. |
| 2 | USB/HID device interface paths | `SetupDiGetDeviceInterfaceDetail` returns a path usable with `CreateFile`; it is distinct from the instance ID now mapped. | Check whether this path is read. Any representation must remain usable for subsequent opens. |
| 2 | Registry alternative reads and enumeration | `RegGetValue` reads values independently of `RegQueryValueEx`; `RegEnumValue` yields values by index and `RegEnumKeyEx` yields subkey names. Current value substitutions do not alter enumerated key names. | Collect exact paths and access methods before widening scope. |
| 2 | WMI alternative clients | The MI API has its own `MI_Session_QueryInstances` path. Other COM implementations or direct provider routes are not established as covered by one `IWbemClassObject::Get` hook. | Determine whether the client uses them and compare only the properties already in the template. |
| 3 | Other network and storage query forms | `GetIfEntry2` can return interface data, including physical address; storage requests may use descriptor/query forms beyond the one currently patched. | Investigate only after evidence of calls or mismatched read-only previews. |
| 3 | Firmware, graphics, display | Windows exposes ACPI tables separately from raw SMBIOS; DXGI exposes adapter descriptors and display APIs expose monitor EDID-derived information. These are not proven identity inputs for this client. | Inventory observed calls first; avoid broad mutation of graphics or firmware results. |
| Boundary | CPUID, TPM and platform trust | `CPUID` is an instruction, not a Win32 call; TPM and UEFI state are platform-backed. A process hook cannot claim to replace these comprehensively. | Keep outside the current agent design; do not equate cosmetic API responses with changed platform identity. |

## Practical conclusion

The largest known gap is **when** data is read, not the number of serial fields
in the UI. Next useful experiment is read-only, phase-labelled coverage logging
from process creation through login, with API category, caller module, timestamp
and outcome but no raw identifiers. Compare the observed surface with the
template preview, then add a narrowly scoped contract test only for a real
inconsistency. There is no evidence-based reason yet to change every candidate.

## Live Gamma observation (2026-09-29)

The user authorized a live launch. The existing `release` Collector was idle,
with Gamma collection disabled and no game process. It was restarted with
`PRICECHECK_TRACE_HARDWARE=1` and `--launch-profile=Gamma`. Collector PID 17236
owned game PID 18156; agent readiness, reader attachment and world entry were
observed. Its hardware trace hit the shared 4096-event limit within seven
seconds because `device_ioctl` logged roughly 3900 identical generic calls.

Only that redundant generic trace event was removed from `disk_identity.cpp`;
the selected IOCTL trace and all identity behavior remain unchanged. The native
build and an isolated full `build.ps1` build succeeded. The isolated Collector
at `workspace/identity-trace-collector` was started with the same Gamma profile
and trace setting after terminating the first test-owned session. Collector
PID 17432 owned game PID 17768. The agent was ready, the reader attached, the
world loaded, and the client remained alive at the final check. Market
collection was **not** enabled; this was a launch/identity observation.

The second trace spans 2026-09-29 12:20:40-12:23:09 UTC, including about two
minutes after world entry, and again reaches 4096 events. It records 594 raw
SMBIOS (`RSMB`) requests, 283 successful adapter-result modifications, 277
`MachineGuid` registry queries and 277 successful volume-information reads.
The WMI property hook installed successfully and the world identity application
event was recorded. There were **zero** recorded calls to the hooked USB/HID
instance-ID path and **zero** mapped WMI property reads in this bounded interval.
No client/protection error was found in the Collector log. None of these counts
establishes whether uninstrumented paths (interface paths, registry enumeration,
MI, kernel reads, CPUID) were used. The normal agent also cannot see earlier
startup reads; the separate early-agent experiment remains the evidence for
those. No replacement-surface expansion is justified by this one Gamma run.

Read-only PE import inspection of this installed client build adds three
specific leads: `lu4-win64-shipping.exe` imports `RegGetValueW`, `clmods64.dll`
imports `RegEnumValueW`, and `lu4.bin` imports `SetupDiGetClassDevsW`. The
`clmods64.dll` import table also lists `GetAdaptersInfo`, which the current
agent covers only after installation. Import presence is **not** proof of a
runtime call or an identity-bearing key; dynamic resolution also means import
absence is not proof of non-use. The next observation should identify the
actual queried key/value or enumerated class and call phase before deciding
whether any process-local representation needs to change.

Next diagnostic improvement: preserve the existing privacy boundary while
making the event budget category-aware, and record only API category, timing and
caller-module class for **observed** alternate reads. Do not infer that a new
identity value is required from an API's existence alone. This isolated build
was not published to `release`.

## Read-only registry ETW check (2026-09-29)

A narrow `Microsoft-Windows-Kernel-Registry` trace was validated with a
controlled `MachineGuid` read, then recorded for 10 seconds while the already
running Gamma `lu4.bin` PID 17768 remained in the world. The game PID produced
880 registry events in that window: 540 key opens and 340 value queries.
Queried value names were `MachineGuid` (80), `EnableDhcp` (80), `Image Path`
(80), `Name` (80), and `Type` (20). Of the `MachineGuid` queries, 40 had a
success status and 40 had a nonzero status; the trace does not establish the
reason for those nonzero results. This confirms a live registry query by the
game process, not a new uncovered identity surface: the current agent already
intercepts its selected `MachineGuid` query path after installation.

Key-path correlation places `Image Path` and `Type` under the Windows
`Microsoft Strong Cryptographic Provider` registry key, and `Name` under its
`Provider Types\Type 001` key. A separate read of the current host registry
(not captured query output) returned `%SystemRoot%\system32\rsaenh.dll`,
type `1`, and `Microsoft Strong Cryptographic Provider`, respectively.
`EnableDhcp` was queried beneath the TCP/IP interface configuration key.
These observations point to standard crypto/network configuration; the trace
does not establish their use as device identity signals.

The ETW provider did not identify the caller module or prove that every query
was used for identity. It also cannot reveal reads made before this capture.
A startup capture would require interrupting the client, so none was performed:
the Gamma market collection was active when the existing session was checked.
No other profile or process was restarted. The standard WPR Registry profile
was too broad for this experiment; the narrow provider captured the necessary
events at a small fraction of its output size. Raw ETW/CSV files remain under
`workspace/identity-registry-probe`: local command policy blocked cleanup.
They contain machine-specific diagnostic data and should not be published.

## References

- [Microsoft: USB interface detail contains a path](https://learn.microsoft.com/en-us/windows/win32/api/setupapi/ns-setupapi-sp_device_interface_detail_data_w)
- [Microsoft: RegGetValue](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-reggetvaluew), [RegEnumValue](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regenumvaluew), [RegEnumKeyEx](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regenumkeyexw)
- [Microsoft: MI_Session_QueryInstances](https://learn.microsoft.com/en-us/windows/win32/api/mi/nf-mi-mi_session_queryinstances)
- [Microsoft: GetIfEntry2](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/getifentry2)
- [Microsoft: firmware table providers](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-enumsystemfirmwaretables)
- [Microsoft: DXGI adapter descriptor](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc), [display target and EDID](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-displayconfig_target_device_name)
- [Microsoft: CPUID intrinsic](https://learn.microsoft.com/en-us/cpp/intrinsics/cpuid-cpuidex), [TPM Base Services](https://learn.microsoft.com/en-us/windows/win32/tbs/tpm-base-services-portal)
