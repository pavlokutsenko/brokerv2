# HardShift launch and identity research

Current Collector status: the GUI-hook loader replaced the temporary adjacent `version.dll` path; direct automated LU4 entry works. Authenticated proxy routing now uses a PID-scoped WFP redirect in the LU4Memory driver and a host HTTP CONNECT broker. Local-proxy world entry and sustained traffic through the supplied external proxy were observed; see `../wfp-proxy/README.md`. The dated observations below describe the successive experiments.

HardShift-specific proxy validation is in `../hardshift-proxy/README.md`: its own proxy launch reached the world through a driver-registered redirect and host-owned local listener.

## Scope

Read-only analysis of `C:\D\HardShift` for adding profile-owned client launch, generated identity, and proxy settings to this repository. No HardShift binary is copied into the collector.

## Observations (2026-09-23)

- HardShift 0.1.0 is a Qt/QML native application (`HardShift.exe`, 35,592,192 bytes) with `assets/HsAgent.dll` and `assets/HsAgent64.dll`. The agents have no exported functions, so loading is likely driven by `DllMain`; this is an inference from the PE export table.
- Configuration lives in `%APPDATA%\HardShift\clients\*.client` and `%APPDATA%\HardShift\templates\*.template`. A client stores `path` and `templateId`. A template stores `proxyEnabled`, `proxyType`, `proxyHost`, `proxyAuth`, `proxyLogin`, `proxyPass`, and `hwGenerated`.
- The installed `random.template` describes a fresh identity on every launch. The QML text says instances bound to the same template share one identity.
- Strings in the executable show `CreateProcessW`, a Windows job object and completion port, root PID tracking, driver-assisted injection (`[inject] IOCTL ...`), a per-PID agent pipe, and `HsAgentHwid` messages with UUID and baseboard serial. The UI exposes a separate machine-wide spoof module and per-process targeted injection.
- Identity fields visible in the executable include system UUID, baseboard serial, NIC MACs, volume identifiers, router MAC, registry MachineGuid and other registry values. This list describes HardShift's apparent surface, not proof that each field works against LU4.
- Proxy strings show SOCKS5, authentication and CONNECT handling. The agent also contains an HTTP CONNECT request and Basic authorization strings. This suggests socket interception inside the target plus a host-side proxy relay; exact flow and child-process coverage require a live trace.
- `version.dll` is absent from `C:\D\HardShift` and from the configured LU4 executable directory at this inspection; no ASCII or UTF-16 `version.dll` literal was found in `HardShift.exe`. The binary does contain `CreateProcessW`, `[inject] IOCTL`, and `HsAgentHwid`; UTF-16 agent DLL names are present. HardShift's path is therefore likely driver-assisted agent injection. This is a static inference, not a verified execution trace; transient DLL staging during launch has not been ruled out. The collector's `version.dll` is our independent implementation choice, not a copy or claim of HardShift's mechanism.
- This collector currently starts the selected executable with `Process.Start(UseShellExecute=true)`, then claims a newly observed `lu4.bin`. It has no identity or network-routing layer. The bundled LU4Memory driver has process read/write/allocate/protect operations, but no general thread-injection IOCTL.

## Implementation boundary

Keep profile and identity/proxy settings in the collector's own models and LocalAppData. A native per-process component would own API/socket interception; UI and domain models should only pass a validated launch configuration. Do not treat environment variables or `ProcessStartInfo` alone as per-process proxy or HWID spoofing.

## Can LU4Memory replace `version.dll`?

The current driver protocol (`native/LU4Memory/include/lu4_protocol.h`, version 3) exposes process base lookup, bounded process reads/writes, target mailbox, packet operations, and target-process virtual memory allocate/protect/free. `dispatch.inc` has no agent-loading or process-start injection command. The existing radar uses memory operations to place a narrow receive stub into the final owned PID; this does not load the identity/proxy agent.

There are two distinct meanings of replacement. The driver could potentially become the **loader** for the existing user-mode agent, removing executable-adjacent `version.dll`, but that needs a new process lifecycle and loading mechanism, PID ownership checks, an agent-ready handshake, rollback and validation on the protected client. The agent DLL remains necessary for the current per-process `GetSystemFirmwareTable`, `RegQueryValueEx`, `DeviceIoControl`, adapter, `SendARP` and Winsock hooks. A driver-only implementation of the same 20 API responses and authenticated HTTP CONNECT would be a separate kernel architecture, not a reuse of today's memory IOCTLs. It would also have different system-wide/filter-driver and compatibility implications. HardShift itself includes `HsAgent64.dll`, so static evidence does not support a driver-only interpretation of that app either.

No driver code was changed for this feasibility review. The verified LU4Memory artifact should remain untouched until a separately designed loading path has passed build and runtime checks. We have not dynamically confirmed HardShift's loading sequence.

## Unknowns

- Which hardware fields LU4 actually reads during login.
- Whether ordinary user-mode injection succeeds under the current Active Anticheat.
- Whether the same executable launches a child process and requires identity inheritance.
- Whether target traffic is TCP only or includes UDP, and what proxy protocol the user needs in practice.

## Next verification

An independent collector implementation now exists in `native/ClientLaunch` and the WPF profile launcher. It uses a `version.dll` startup proxy to load an agent only when the launcher provides environment configuration. The agent hooks SMBIOS UUID and available system/board/chassis/processor/memory identifiers, registry MachineGuid/HwProfileGuid/ProductId, adapter MACs, volume serial, and storage descriptor/disk layout IDs. It redirects IPv4 TCP `connect`, `WSAConnect` and `ConnectEx` through HTTP CONNECT with Basic authentication. It does not patch the collector's receive-hook site or `ws2_32!send`; its own relay uses `WSASend`/`WSARecv`.

A local smoke process confirmed the generated registry/volume/firmware identifiers and HTTP CONNECT authentication/tunnel. WMI-specific reads, OS resolver behavior, non-TCP game traffic, and behavior after character login still need checking. Non-loopback UDP and IPv6 connections are rejected while the proxy is enabled to avoid direct socket fallback; OS DNS resolution may still occur outside the target process.

## Live LU4 validation (2026-09-23)

- HWID-only launch: the collector claimed final `lu4.bin` PID 11052, its agent logged `ready`, and the existing receive-hook installed before login. The intermediate shipping process exited normally.
- Authenticated HTTP-proxy launch: the collector claimed `lu4.bin` PID 16232 and again installed the receive-hook. A temporary local HTTP proxy checked the Basic credentials and observed CONNECT requests for `external-address-1:53` and `external-address-2:11000` from that launch. The proxy was stopped after the test; the temporary proxy settings and password were cleared from the profile. The profile retains “new HWID on launch.”
- Both tests stopped the owned game client through the collector; there is no active test client now. The staged test `version.dll` was removed with the hash-checked rollback script. Broker cycles after character login were not exercised.

The launcher refuses to overwrite an unrelated executable-adjacent `version.dll`. Its own staged file can be removed after all LU4 clients stop with `native/ClientLaunch/remove.ps1 -GameBinDir <client-bin-directory>`; the script checks the file hash before removing it.

## Launch templates (2026-09-23)

The collector stores reusable launch templates in `%LOCALAPPDATA%\PriceCheckCollector\launch-templates.json`. Each collector profile stores one template ID; the special “Без шаблона” choice launches without the identity/proxy agent. Existing per-profile HWID/proxy settings migrate to a template on first launch of this version. The dedicated Templates tab shows 20 generated values plus a read-only scan of local hardware and hook coverage; see `../hwid-surface/README.md` for the complete field list and limitations. A template can keep values fixed or rotate them before each launch; in rotation mode, the editor shows the last attempted launch values. HTTP CONNECT with Basic authentication is optional and configured independently from HWID. Passwords are DPAPI-protected in the template store. Editing a template changes future launches only; a running client retains its launch snapshot. The driver-backed receive hook remains attached to the final owned `lu4.bin` PID.

UI verification created and saved a temporary template, then removed it. After restoring the original migrated template and profile binding, a template-driven HWID-only launch claimed final `lu4.bin` PID 15112: the per-PID agent log reported `ready`, and the collector showed “Радар готов · можно входить”. The client was stopped through the collector and the staged `version.dll` was removed. No game process remains running.

After extending the agent to 20 values, the native build and child-process smoke passed SMBIOS, registry, volume, storage layout, gateway SendARP and HTTP CONNECT assertions. The Collector's Templates tab scanned 84 local rows including disk 0 serial/GPT and the gateway MAC. A fresh template-driven LU4 launch claimed final PID 10076, reported agent `ready` and installed the receive-hook before login. It was stopped via the Collector; the hash-checked rollback removed the staged `version.dll`. This proves startup integration on this machine, not that LU4 reads every configured identity field.

## Live HardShift trace (2026-09-23)

HardShift was already running with its `Random` template, proxy disabled. Its diagnostics in `%TEMP%\HardShift_diag.log` record a fresh launch at 03:43:41: root `lu4-win64-shipping.exe` PID 15708, `d3d9.dll` marker on the root, child `lu4.bin` PID 13824 at 03:43:44, then `applyCustomProfile OK per-pid`, `applyCustomProfile OK wildcard`, `patchPhysSmbios OK verify=3 bytes=2591`, and an `HsAgentHwid` entry for PID 13824. The root later exited while the child remained live.

`VirtualQueryEx` plus `GetMappedFileNameW` on the live child showed an image mapping of `C:\D\HardShift\assets\HsAgent64.dll`. The only mapped `version.dll` was `C:\Windows\System32\version.dll`; no executable-adjacent `version.dll` file appeared. This confirms HardShift's per-process agent was present in the protected child without that staging method. It does not identify the exact IOCTL or kernel execution primitive that loaded the agent.

Static disassembly of `HardShift.exe` locates its agent-injection wrapper at VA `0x140CA19D0`. It calls `DeviceIoControl` with code `0x222140`, a `0x418`-byte input and a `0x18`-byte output. The input begins with a 32-bit PID and a zero DWORD, followed by two fixed `0x208`-byte UTF-16 path slots. The result checks a status DWORD at offset 0 and reads a thread ID at offset 4; a further NTSTATUS-like field is logged from offset 16. Its sole direct caller is at VA `0x140C4C4AC`. The two paths may be the 32/64-bit agents, but that mapping is an inference. The live log did not print an `[inject]` line, so the static wrapper cannot by itself prove the exact IOCTL used in this run. The child's mapped `HsAgent64.dll` is the separate runtime observation.

HardShift's startup log also shows it fetched `eneio-driver` (32,504 bytes) and `hscore-driver` (105,464 bytes), loaded the first as a temporary vulnerable driver, manually mapped the second into the kernel, pinged the mapped driver, then unloaded the temporary one. Its diagnostic text reports changes to PiDDB/module metadata. The mapped driver is not a normal service entry; absence from `Win32_SystemDriver` is expected for this path. We should not reproduce its concealment steps. The useful architecture finding is a kernel component with a per-PID profile command plus a user-mode `HsAgent64.dll` for process-local behavior.

The collector's tested `ObOpenObjectByPointer`/`CreateRemoteThread` candidate failed against the protected child (`GrantedAccess=0x1400`), so HardShift's successful load does not validate that candidate. A trace of HardShift's actual agent-load IOCTL or a separate safe kernel execution design is still required before replacing the collector's `version.dll`.

Read-only inspection of HardShift's committed private user memory through the verified LU4Memory read IOCTL did not find a retained PE image of the fetched `hscore-driver`; about 55 MB of readable private pages were scanned, and no driver bytes were saved. The driver was manually mapped at startup and the original download buffer appears to have been released or transformed. Its handler for `0x222140` therefore remains unverified.
