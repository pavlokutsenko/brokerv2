# Client and protection traffic observation

Scope: read-only attribution of identity queries and network channels during a
Gamma launch. Do not publish credentials, addresses, raw packets, or identity
values. Market collection must remain disabled during this experiment.

## Established before this run

- The 2026-09-24 registry stack captures attributed early `MachineGuid` reads
  to graphics/crypto components and an audio UUID read to audio components;
  they did not show `clmods64.dll` on those stacks. One successful, direct
  `ComputerName` read through `clmods64.dll -> ntdll!NtQueryValueKey` was
  confirmed. See
  [registry callers](../two-client-isolation/observations/2026-09-24-registry-callstacks.md).
- A separate 2026-09-29 bounded hardware trace contained 594 `RSMB` reads.
  Its immediate-caller classifier marked every one as a module other than
  `lu4.bin` or `clmods64.dll`. This is compatible with the earlier graphics
  stacks, but the classifier alone does not name the third-party module.
- The default late agent covers selected Win32 `ComputerName` registry reads,
  but not that established earlier native call. The separate native registry
  trace hook is opt-in for an early-root experiment and does not establish
  production coverage. Neither observation proves the value was transmitted
  or caused a server decision.
- The protected Gamma world handshake has confirmed 35-to-67 and 37-to-69
  source/wire length forms. See
  [world handshake](../gamma-world-hwid-67/README.md) and
  [world buffer](../two-client-isolation/observations/2026-09-24-world-buffer-layout.md).
  These are evidence for an identity-bearing game message, not an inventory
  of all protection traffic.

## 2026-09-29 controlled launch

The initially running release Collector PID 14900 owned Gamma PID 7068. The
saved Gamma profile had `CollectionEnabled=false`. That session was closed
through the Collector window, which also closed its game process; the separate
PriceCheck server was not touched. Release Collector PID 9020 then launched
Gamma (launcher PID 17008, game PID 3164) with only the existing
`PRICECHECK_TRACE_PORTS=1` destination-port metadata flag. Collection stayed
disabled. Auto-login was also disabled, so this attempt stopped before world
entry; no profile setting or protection mechanism was changed.

The bounded connection poll took 59 samples over 75 seconds. It observed one
external Collector connection for the launcher phase and a loopback Collector
connection, but no established connection owned by game PID 3164. The port
metadata recorded two DNS destinations and one additional launcher TCP
destination. A previous successful Gamma trace, captured while market
collection was active, recorded login, HTTPS-port and world-port destinations
through the same relay. Those prior packet-size totals include long market
traffic and cannot be attributed solely to login or protection.

The running `AAErrorPort` service exposed no TCP or UDP endpoint in the
snapshots.
Its executable statically imports `DeviceIoControl`, basic system-info calls,
and no direct Winsock/WinHTTP entry points in the inspected import table.
`clmods64.dll` statically imports `RegEnumValueW`, `RegOpenKeyExW`,
`GetAdaptersInfo`, `DeviceIoControl`, `WSASocketW`, and process/system-info
calls. Imports are potential call paths, not runtime proof; dynamically
resolved APIs and kernel-origin traffic are outside this inventory. Windows
did not expose the protected game's loaded module list through `Get-Process`
or `tasklist`, so the current session cannot attribute every in-process call
to a specific DLL.

The active `PRProt` driver image (`active64.sys`) statically imports
`ZwQueryValueKey`, `ZwQuerySystemInformation`, process/thread information
queries and `KeStackAttachProcess`. Its import table alone says nothing about
which registry values or system classes it requested in this run. No direct
network-stack import was seen in the inspected subset; this is not proof that
the driver or protection stack cannot cause network traffic elsewhere.

No raw network payload was captured in this controlled run. The relay's
destination-port log and TCP snapshots establish channels, not message
contents. The existing world-handshake implementation establishes one
identity-bearing send, but there is no evidence here that `ComputerName`, USB,
WMI, CPUID or other candidate surfaces are sent on any particular channel.

## Evidence boundary

Do not infer anticheat-specific payload from port numbers, TCP read sizes or
static imports. A separate caller-attributed registry trace would be needed
to assign new identity reads to `clmods64.dll`; avoid another broad raw
capture until prior temporary ETW files can be cleaned up.

## Auto-login world observation (same day)

The user explicitly authorized enabling auto-login. The prior Collector was
closed normally. Only Gamma's `AutoLoginEnabled` flag was changed from false
to true; Gamma and Black both retained `CollectionEnabled=false`. Release
Collector PID 12344 launched Gamma PID 12352 with the destination-port trace.
The agent became ready, the saved world-identity application status reported
success, and the reader reported the world loaded at 16:13:41 Europe/Kiev.
There were no warning or error entries through the first world observation.

The launcher requested two DNS destinations and one other external TCP port.
The game then requested its login port at 16:13:10 and Gamma's world port at
16:13:16. A 125-second, 97-sample TCP snapshot saw the corresponding external
Collector relay connections and the game's loopback leg. A separate public
port-443 connection belonged to the Collector process but was **not** in the
per-game destination-port trace; it must not be called anticheat traffic on
this evidence. `AAErrorPort` exposed no separate TCP endpoint in the poll.
Exact read, broker-pass and route-start counts in the Collector log stayed at
zero; passive radar/shop-state notices still appeared with collection off.

These are endpoint and state observations, not packet contents. The known
protected world identity send remains the only message with a documented
identity-bearing structure. The current trace cannot show what, if anything,
the client or protection module sends later as application payload.

## Bounded traffic-length diagnostic

With auto-login on and collection still off, release Collector PID 17400
launched Gamma PID 3120 using the existing hardware-category and proxy-length
traces. The launcher again used DNS and its service port; the game used only
the login and Gamma world ports in this run. The login relay was open for
about six seconds and, in that interval, forwarded 222 bytes outward and
2,256 bytes inward. The world relay opened immediately afterward. By the
16:21:15 sample (about 3m45s after it opened), the world relay had forwarded
4,043 bytes outward and 1,037,824 bytes inward, while the market collector
remained disabled. These are stream-byte counts at the relay, not parsed game
messages or proof that any subset came from anticheat code.

The native trace recorded a 37-byte upper world send before its shared
4,096-event budget filled; the relay then observed a 69-byte read on the
world stream, consistent with the previously validated 37-to-69 protected
form. Its identity-application status reported success and the game entered
the world. The same stream trace saw an initial 15-byte read, one 141-byte
read, many 7-byte reads concentrated near world entry, and eight 27-byte
reads. After the first two, the 27-byte reads were spaced roughly 30 seconds
apart. A prior caller-attributed test linked the same periodic 27-byte send
form to `clmods64.dll` and driver IOCTL `0x222160`; the current relay trace
alone cannot re-attribute each read or reveal the message contents.

The published release still logs a generic `device_ioctl` event, so 3,904 of
the native trace's 4,096 entries were consumed by that category within 7.2
seconds. In that short window it also recorded 30 `RSMB` reads (all classified
as immediate calls from modules other than `clmods64.dll` and `lu4.bin`), 16
adapter modifications, and 15 selected registry/volume reads. There were no
mapped WMI or USB/HID instance-ID reads in the bounded trace; the short window
does not support a claim that those APIs were unused during the whole login.

The diagnostic Collector was closed normally to stop its continuously
growing length trace. The ordinary release Collector PID 300 then launched
Gamma PID 1564 with no diagnostic environment flags. At 16:23:55 Europe/Kiev
the world was loaded, identity application reported success, and the log had
no warning/error, broker pass or exact shop read. `AutoLoginEnabled=true` and
`CollectionEnabled=false` remained saved for Gamma. The separate server host
was not changed. These PIDs are historical and must be rediscovered.

## Driver import callers (2026-09-29)

Read-only Ghidra analysis of the saved `active64.sys` project was checked
against the currently running `PRProt` image by matching MD5. The visible
`ZwQuerySystemInformation` call requests class `0x0B` and its callers use the
result as a loaded-kernel-module list. Visible `ZwQueryInformationProcess`
callers use classes `0` and `0x1A` while inspecting process state. These are
module/process introspection paths, not evidence of a CPU or USB identity
query. The direct `ZwQueryValueKey` references are in generic `CmRegUtil`
helpers; this static pass did not establish a concrete queried value or a
runtime call from the protection path. See
[driver import callers](observations/2026-09-29-driver-import-callers.md).

The same analyzed image contains nine `CPUID` instructions in three functions.
Their visible branches inspect CPU vendor/features, Hyper-V capability leaves,
and a timing path; no processor serial or brand-string leaf was found in those
instructions. Static code presence does not prove execution or transmission.
See the linked note for the exact evidence boundary.

This does not inventory dynamically resolved functions, other registry paths,
CPUID in other modules, or traffic contents. It does not change the earlier
caller-attributed `clmods64.dll` `ComputerName` finding.

## CPU interception boundary and cleanup

The discovered `CPUID` instructions execute directly in the third-party
kernel driver. `ClientLaunch` is a user-mode agent and `LU4Memory` has no
hypervisor/CPUID-exit facility; adding a Win32/IOCTL hook would not intercept
those instructions. No CPU hook or protection change was installed, and this
analysis does not establish a processor serial read or network transmission.
The previously tested guest VM was rejected by the game launcher, so that
path is not a verified replacement for the working host session.

After the user requested cleanup, 5.12 GiB of disposable-looking ignored
ETW/CSV captures, isolated test builds and package-recovery copies were
identified under `workspace`. No process was running from those directories.
The local deletion command was blocked by the execution policy before any
file was removed. The candidates therefore remain on disk; the working
`release`, market databases and Ghidra project were not touched.
