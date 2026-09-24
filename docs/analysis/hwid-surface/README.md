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
- Twenty generated template values: system UUID/serial, board serial, chassis serial, processor ID/serial, memory serial, volume serial, disk serial, disk GPT GUID/MBR signature, MAC base, gateway MAC, MachineGuid, HwProfileGuid, Windows ProductId, SusClientId, VideoIdentifier, registry ComputerName and Windows InstallDate. SMBIOS/storage strings are overwritten within the original field length, so previews adapt to the current machine. GPT or MBR applies according to the disk partition style. The same serial seed is reused for multiple memory modules/disks when queried; this is a known consistency limitation. The gateway MAC hook applies only to `SendARP` calls for the IPv4 gateway detected at launch.
- Local native smoke test verifies SMBIOS UUID, processor ID and serial fields, registry MachineGuid/HwProfileGuid/ProductId/ComputerName/InstallDate, volume serial, disk descriptor serial, disk layout ID, gateway SendARP and HTTP CONNECT authentication. The Templates tab scanned 84 local rows after the 20-field update. A live LU4 launch through the expanded template claimed final `lu4.bin` PID 10076, reported agent `ready`, and installed the collector receive-hook. It was stopped and the staged `version.dll` removed.

## Detected only / open work

BIOS vendor/version, system and board manufacturer/model, SQM values, processor model/revision, monitor EDID, GPU/audio PCI IDs, USB/HID instance IDs, TPM/Secure Boot, and WMI provider responses are not spoofed by this agent. Registry InstallDate, SusClientId and ComputerName are hooked only when read through `RegQueryValueEx`; the gateway MAC only when read through `SendARP`. The TraceX scripts change several of the other fields globally and persistently; importing that behavior into process templates would be misleading. Direct kernel/driver hardware reads also bypass this user-mode agent. Coverage for LU4 after character login and on other physical machines remains unverified.

## Live login sample

In one no-proxy LU4 run, the user entered a character after the agent and driver-backed radar were ready. Opt-in hook tracing saw repeated successful `GetAdaptersAddresses` calls after agent installation, including around login, and no calls to the other instrumented identity APIs. The GUI-hook loader starts after the game window appears, so it misses earlier startup reads. This sample does not prove which identities the game or anticheat ultimately uses, nor that all per-process replacement values reached its checks. See `../driver-agent-load/PROGRESS.md` for the simultaneous proxy failure.

In a later fully automated direct login, `GetAdaptersAddresses` returned successfully seven times after the agent loaded. The hook reported two adapter MAC values overwritten on each call (`adapters_modified=2`). No later identity surface was observed among the instrumented APIs in that sample. The count verifies this API's replacement in the live client, not the game's eventual account or device decision.

An experimental DLL load before the child's main thread observed two `GetSystemFirmwareTable('RSMB')` calls before the game window, which the normal GUI-hook loader cannot reach. That early-loaded client exited before a stable game window, including in a control with no identity/proxy hooks; it is not a viable default loader. See `../early-agent/README.md`.
