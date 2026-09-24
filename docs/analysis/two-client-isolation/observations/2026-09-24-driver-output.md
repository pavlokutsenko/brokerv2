# Early anticheat driver output

The test-only early IAT capture reads the second client's own successful
`DeviceIoControl(0x222044)` and `0x22201C` responses. It does not alter them.
The preceding `0x74080` call in the same trace is `SMART_GET_VERSION`
according to the installed Windows SDK `ntdddisk.h` (`IOCTL_DISK_BASE`,
function `0x20`, buffered, read access). It queries SMART support; the
early trace recorded no `SMART_RCV_DRIVE_DATA` call. This early call alone
does not demonstrate a disk-serial read by the anticheat.
Four concurrent Black launches with Gamma in the world repeated the server
disconnect. In three consecutive launches, the full `0x222044` output body
changed, but its final eight bytes were identical. One of those three launches
regenerated every template identity value. All three early SMBIOS and adapter
replacement counters were positive. The `0x22201C` 176-byte output changed
overall; 10 of 11 sixteen-byte block hashes changed, while block eight stayed
the same. Captures are limited to the opt-in flag
`PRICECHECK_EARLY_044_CAPTURE=1` and local ignored logs.

Static inspection of `active64.sys` shows the `0x222044` handler returns a
driver-managed data blob with a one-byte prefix and an eight-byte trailer. The
trailer is read from a runtime global in the driver's large `.data` region.
That global is written in an obfuscated initialization function which also
opens `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows`. The
decompiler cannot recover the branch leading to the write, so the registry
operation does not prove the trailer is registry-derived. The repeated
trailer is a real shared driver value on this boot, but its meaning,
stability across reboots and presence in the encrypted world request are
unknown. It may be an internal token or structural value rather than an HWID.
The fixed `0x22201C` block may likewise be protocol metadata.

An independent read-only `DeviceIoControl` call from a PowerShell diagnostic
process opened `\\.\PRProt`, but both codes returned Win32 error 31 with no
bytes. A portable external one-call probe is not a valid second-PC comparator
without reproducing the driver's client registration path.

The game's world connection still closes after a 15-byte client request, a
13-byte server reply and a 69-byte client follow-up when Gamma is already
occupied by the first process. Successful solo Black runs receive a further
193-byte server reply. No local IOCTL failure or plaintext machine identifier
has been shown to explain this difference. Do not substitute arbitrary bytes
into driver responses or claim that the repeated trailer is the rejection key.

All experimental client hooks remain behind environment flags. The release
Collector was started without those flags. The normal first Gamma client
remained active during the above concurrent probes.
