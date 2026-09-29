# Driver import caller classification

Date: 2026-09-29. Method: read-only Ghidra headless pass over the existing
`AntiCheatIoctl` project, with no client restart or collection. The project's
`active64.sys` MD5 matched the current file used by the running `PRProt`
service (`8225638d5b9781b8540b181a4d8911f2`). The local script and raw
decompiler output are under `workspace/GhidraScripts/InspectIdentityImports.java`
and `workspace/identity-import-callers.txt`; they are not published here.

- The visible `ZwQuerySystemInformation` call passes information class `0x0B`.
  Its callers enumerate loaded kernel modules, compare module names or bases,
  and obtain image ranges. This is module introspection, not a demonstrated
  machine identifier read.
- The visible `ZwQueryInformationProcess` calls use classes `0` and `0x1A` in
  process-inspection helpers. This does not show a CPU, USB/HID or WMI query.
- `ZwQueryValueKey` appears in generic `CmRegUtil` value readers. The inspected
  direct references did not identify a concrete key/value or prove that a
  protection-specific runtime path invokes those helpers. An import alone
  must not be counted as an observed identity read.
- There was no `ZwEnumerateValueKey` import in this image. This does not rule
  out another registry API, dynamic resolution, or another component.
- Nine analyzed `CPUID` instructions belong to three functions. The first
  examines vendor, feature and Hyper-V capability leaves (`0`, `1`,
  `0x40000000`, `0x40000001`, `0x40000003`). The second checks vendor as part
  of a timing/MSR path. The third derives CPU instruction-feature flags from
  leaves `0`, `1`, and `7`. No processor-serial leaf `3` or brand-string
  leaves `0x80000002`-`0x80000004` were observed in these functions. This is
  evidence that the driver contains CPU/virtualization capability checks,
  not evidence that a stable CPU identifier is sent to a server. Ghidra's
  decompiler marked some `CPUID` blocks unreachable; runtime execution of
  each branch was not verified.

Microsoft's [Hyper-V feature-discovery specification](https://learn.microsoft.com/en-us/virtualization/hyper-v-on-windows/tlfs/feature-discovery)
defines the `0x40000000`, `0x40000001`, and `0x40000003` leaves, matching the
vendor, interface and feature checks visible in this function.

The current Gamma process remained in the world, and both saved profiles
retained `CollectionEnabled=false`. No network contents or credentials were
captured. The next useful step is caller-attributed evidence for a specific
unknown identity read, not additional broad import inventory.
