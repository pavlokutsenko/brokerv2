# HWID surface audit

Read-only source audit on 2026-09-23. Targets: `C:\D\HardShift` and `C:\Users\Pavel\Desktop\HWID Spoofer - FINAL`. Do not run the second application's scripts or bundled AMIDEWIN, VolumeID, driver, cleaner, VPN installer, or uninstallers during this audit.

## HardShift evidence

`HardShift.exe` contains fields for system UUID, chassis/baseboard serial, processor ID/serial, memory serial, disk 0/1 serial, GPT GUID and MBR signature, router MAC, volume IDs, and registry `MachineGuid`, `HwProfileGuid`, `ProductId`, `SQMClientId`, `SusClientId`, video identifier, computer name, username, install date, and processor revision. It also contains `INSTALL NTSTATUS` lines naming firmware, disk, network, NIC, and registry driver modules. These are binary strings, not proof that each field is successfully spoofed on LU4 or on every computer. The per-process and machine-wide modules may cover different sets.

## TraceX script evidence

The second app is a PowerShell script bundle. The core script calls AMIDEWIN for SMBIOS chassis/baseboard/product/system serial and UUID. `change_disk_ids.ps1` calls VolumeID for volume IDs. `change_registry_hwids.ps1` writes `HwProfileGuid` and `MachineGuid`. Other scripts modify NIC registry settings, display EDID, selected USB/HID device registry values, and selected PCI hardware IDs. The advanced features description overstates some actions: its CPU/motherboard section explicitly writes only `BaseBoardVersion` and `SystemProductName`; GPU/audio section changes selected device ID strings in the registry. Those operations are machine-wide and persistent, unlike the collector's per-process agent.

## Collector boundary

The collector scans identifiers on the local machine at runtime and labels the source and coverage. Templates hold generated replacement values for process-scoped hooks; scanned originals are not persisted. A template may be reused on another computer, but availability and effective field length can differ by firmware/device. Values that are merely detected must not be shown as already spoofed. Kernel/firmware, WMI, monitor EDID, USB/HID and global system changes are separate surfaces and cannot be promised by user-mode hooks alone.

## Implemented and verified

- Separate WPF Templates tab; a collector profile selects one template or no template. An existing “прежний запуск” name migrates to “случайный HWID”; it described the old settings import, not a process history.
- Runtime scan via Win32 SMBIOS, registry, network APIs, volume information, disk storage descriptor and layout IOCTLs, monitor EDID registry data, and bounded PCI/USB/HID registry enumeration. It shows the local original, the template's effective preview, and whether the current agent intercepts that source. The scan is read-only and stays in memory.
- Twenty-three generated template values: system UUID/serial, board serial, chassis serial, processor ID/serial, memory serial, volume serial, disk serial, disk GPT GUID/MBR signature, MAC base, gateway MAC, MachineGuid, HwProfileGuid, Windows ProductId, SusClientId, VideoIdentifier, registry ComputerName and Windows InstallDate, SQM MachineId, processor model and processor revision. SMBIOS/storage strings are overwritten within the original field length, so previews adapt to the current machine. GPT or MBR applies according to the disk partition style. The same serial seed is reused for multiple memory modules/disks when queried; this is a known consistency limitation. The gateway MAC hook applies only to `SendARP` calls for the IPv4 gateway detected at launch.
- Local native smoke test verifies SMBIOS UUID, processor ID and serial fields, registry MachineGuid/HwProfileGuid/ProductId/ComputerName/InstallDate, volume serial, disk descriptor serial, disk layout ID, gateway SendARP and HTTP CONNECT authentication. The Templates tab scanned 84 local rows after the 20-field update. A live LU4 launch through the expanded template claimed final `lu4.bin` PID 10076, reported agent `ready`, and installed the collector receive-hook. It was stopped and the staged `version.dll` removed.

## Detected only / open work

BIOS vendor/version, system and board manufacturer/model, other SQM values, monitor EDID, GPU/audio PCI IDs, TPM/Secure Boot, and unlisted WMI properties are not spoofed by this agent. USB/HID and selected WMI API responses gained process hooks in the later isolated test below; registry instance key names remain unchanged. Registry InstallDate, SusClientId, ComputerName, SQM MachineId and the processor model/revision are hooked only when read through `RegQueryValueEx`; the gateway MAC only when read through `SendARP`. The TraceX scripts change several of the other fields globally and persistently; importing that behavior into process templates would be misleading. Direct kernel/driver hardware reads also bypass this user-mode agent. Coverage for LU4 after character login and on other physical machines remains unverified.

The earlier 84-row scan is not a list of 84 independently overridable values. Its
USB/HID rows were registry instance *key names*; GPU/audio rows come from
device-enumeration keys; the WMI and firmware/TPM/Secure Boot surfaces have
separate providers. Extending `RegQueryValueEx` substitutions cannot change
those instance paths or kernel-visible state. Broadening the process agent
requires separate, source-specific interception and consistency checks; the
UI must continue to label unverified rows as detected only. No machine-wide
identity change or live client modification was made in this audit.

## Process-scoped registry expansion (2026-09-29)

The agent now substitutes `ProcessorNameString` and `Update Revision` only under
`HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0`, and `MachineId` only
under `HKLM\SOFTWARE\Microsoft\SQMClient`. It resolves the queried key path
before replacing these otherwise generic value names. New template fields are
generated for them; missing fields in existing templates are derived from the
stored world-identity seed so repeated loads keep the same identity. The
Templates tab shows all 23 generated values and marks only these exact registry
read paths as hooked.

An isolated child-process smoke test called both Unicode and ANSI
`RegQueryValueEx` for all three values and received the configured replacements.
The same value name on an unrelated registry key was not replaced. Launcher and
collector UI smoke tests and an isolated `build.ps1` build passed. This verifies
the hook behavior for those calls, not that LU4 makes those calls or accepts the
result. No running collector or game was restarted, and no release installation
or machine-wide registry change was made. USB/HID instance paths, WMI, CPUID,
firmware and driver-level reads remain outside this expansion.

## USB/HID, WMI and CPUID feasibility (2026-09-29)

The current `LU4Memory` driver exposes bounded process-memory, packet and
per-process network-routing operations. It is not a device-enumeration filter,
WMI provider, or hypervisor. Adding an IOCTL alone would not change values
returned to the game by unrelated Windows APIs.

- USB/HID instance IDs are returned by SetupAPI and Configuration Manager
  (`SetupDiGetDeviceInstanceId`, `CM_Get_Device_ID`, and the unified device
  property model), while the current inventory displays registry instance key
  names. A process-local implementation would need a stable original-to-fake
  mapping and reverse mapping for calls that subsequently open a device by ID;
  changing registry value data alone does not cover these paths. No such hook
  or live-client verification exists yet.
- WMI results are delivered to the caller as COM objects, usually from an
  out-of-process provider. Process-local substitution must target the returned
  class/property values and stay consistent with the native API and registry
  replacements; modifying this driver's memory APIs does not alter the WMI
  provider's results. The relevant LU4 WMI classes/properties have not been
  observed, so no production WMI substitution has been added.
- `CPUID` is a CPU instruction, not a Win32 or driver IOCTL call. The current
  driver cannot interpose on arbitrary in-process `CPUID` execution. A
  virtualization layer can configure CPUID exits for a guest, but this is a
  different execution architecture, not a safe extension of the verified
  `LU4Memory` driver. No CPUID spoofing is claimed or installed.

This is a feasibility boundary, not a completed implementation. The existing
agent, driver, release, and running LU4 process were not modified during this
check. Next work should first identify the exact USB/HID and WMI query paths
used by the target process in an isolated test, then add only source-specific
process hooks with consistency and negative-path tests. CPUID requires a
separate virtualization decision; it must not be represented as covered by
ordinary driver hooks.
The implementation status in the next section supersedes the USB/HID and WMI
feasibility status above; the CPUID boundary still applies.

## USB/HID and WMI process hooks (2026-09-29)

The native agent now maps `USB\...` and `HID\...` instance IDs in process memory
through `SetupDiGetDeviceInstanceIdW/A`, `SetupDiGetDevicePropertyW`,
`CM_Get_Device_IDW/A`, and `CM_Get_DevNode_PropertyW`. The replacement keeps the
enumerator/device prefix and instance-string length, and is deterministic from
the template's system UUID plus the original ID. `SetupDiOpenDeviceInfoW/A` and
`CM_Locate_DevNodeW/A` translate previously returned IDs back when the process
opens the same device. The mapper is idempotent because SetupAPI can call a
hooked Configuration Manager API internally. Unrelated PCI IDs pass through.
The Templates scanner now reads actual SetupAPI IDs, rather than presenting
registry instance key names as if those were the intercepted API values.

For WMI, the agent obtains an `IWbemClassObject::Get` implementation through a
local `Win32_ComputerSystemProduct` query and hooks string properties already
represented in the template: system UUID/serial, baseboard serial, processor
ID/name, memory and disk serials, logical-volume serial, computer name, and
USB/HID `PNPDeviceID`/`DeviceID`. Other WMI properties pass through. The
Templates scan previews these specific WMI values. No machine-wide WMI
provider, device driver, registry instance key, or CPUID behavior is changed.

Isolated `ClientLaunch.Smoke` confirmed that a present USB/HID devnode returns
the same mapped ID through ANSI/Unicode Configuration Manager, ANSI/Unicode
SetupAPI and device-property queries; it can be reopened by that ID. A WMI
`Win32_PnPEntity.PNPDeviceID`
read agreed with SetupAPI. WMI system UUID and baseboard serial matched the
template, while a PCI device ID and unrelated WMI OS caption remained
unchanged. The native build and `Launcher.Smoke` also passed. The full desktop
build was published only into `workspace/usb-wmi-build`, not the installed
`release` directory. These checks do not prove that the protected LU4 client
uses these particular APIs or accepts their values; device-interface paths,
raw registry enumeration, alternative WMI implementations, kernel reads and
CPUID remain uncovered.

### Release publication

On 2026-09-29, the user requested a release build for testing. `build.ps1`
completed and published Collector and Launcher with the same build ID
`4f3660b22e0e4ca284632ccfedafbbde` under `release/`. The published
Collector agent SHA-256 matched `native/ClientLaunch/build/x64` exactly. The
Collector portable archive is
`release/packages/PriceCheckCollector-win-x64-20260929-150703.zip`; package
verification passed. Publication does not constitute a protected-client test.

### Host virtualization check

After virtualization was authorized, a read-only host check on 2026-09-29
found an AMD Ryzen 7 2700X with VM monitor and SLAT support, but
`VirtualizationFirmwareEnabled=False`; `systeminfo` also reported
`Virtualization Enabled In Firmware: No`. Hyper-V and Windows Hypervisor
Platform are disabled, while Virtual Machine Platform is enabled. No Hyper-V
management cmdlet, common VM executable, or guest image in the checked VM
directories was found. These checks did not change Windows features, BIOS,
the driver, or running processes. A CPUID-exit experiment needs firmware SVM
enabled, a supported hypervisor, a guest OS and a separate compatibility test;
it must not be attempted on the ongoing collector session.

## Live login sample

In one no-proxy LU4 run, the user entered a character after the agent and driver-backed radar were ready. Opt-in hook tracing saw repeated successful `GetAdaptersAddresses` calls after agent installation, including around login, and no calls to the other instrumented identity APIs. The GUI-hook loader starts after the game window appears, so it misses earlier startup reads. This sample does not prove which identities the game or anticheat ultimately uses, nor that all per-process replacement values reached its checks. See `../driver-agent-load/PROGRESS.md` for the simultaneous proxy failure.

In a later fully automated direct login, `GetAdaptersAddresses` returned successfully seven times after the agent loaded. The hook reported two adapter MAC values overwritten on each call (`adapters_modified=2`). No later identity surface was observed among the instrumented APIs in that sample. The count verifies this API's replacement in the live client, not the game's eventual account or device decision.

An experimental DLL load before the child's main thread observed two `GetSystemFirmwareTable('RSMB')` calls before the game window, which the normal GUI-hook loader cannot reach. That early-loaded client exited before a stable game window, including in a control with no identity/proxy hooks; it is not a viable default loader. See `../early-agent/README.md`.
