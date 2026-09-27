# Driver, client updates and concurrent profiles

2026-09-25. Audit of the new recurring collector path.

All game memory in the active C# reader and packaged Python movement/broker/shop
workers goes through `\\.\LU4Memory`: `Lu4Device` and `Lu4MemoryClient` use
DeviceIoControl. No ReadProcessMemory fallback was introduced. Driver access is
not a substitute for resolving/validating client layouts.

| Part | Resolution and validation | Update limitation |
|---|---|---|
| Receive radar | Unique executable-section signature; original instruction guard | Changed instruction/register layout needs a new validated signature/stub |
| Send action/broker | PID/base-bound session resolver, original send/post bytes checked | Native RVAs remain build-specific |
| Broker events | UObject/UFunction discovery and parameter layout checks | Reflection ABI and names/indices can change |
| World and movement | Module-relative profile; PE timestamp/size, class, owner, UFunction name/parameter size and capsule checks | Unknown builds fail before movement; not automatically compatible |
| Obstacles | Reflected field names/sizes, saved Giran geometry, native capsule checks | Map changes and new cities need a validated map |
| Exact price wire | Framing/row-size/count checks; int64 decode | Only proven A1 sell layout is exact; other shop layouts stay unverified |

Current profile is timestamp `956E0D97`, image `DCEB000`. Do not remove guards or
claim arbitrary future client updates work. A client update requires a read-only
compatibility/discovery pass and validation of the changed profile/map first.

## Concurrency

- The driver creates a nonexclusive device. C# now opens with read/write sharing,
  matching Python. Each IOCTL carries PID/address and an IRP-local buffer. C# also
  verifies response PID/address/size, not just returned byte count.
- Packet encryption has `g_CryptoMutex`: a brief serialization of crypto state
  mutation, with target-process/PID-slot validation. Different windows do not
  share rolling keys, packet buffers or game hooks. No driver replacement needed.
- Legacy `g_TargetCommand` is ONE global mailbox. The production cycle does NOT
  call Submit/Claim/Complete/QueryTarget; it uses each PID's in-process command
  cave. The legacy mailbox is not advertised as multi-client capable.
- One active worker per profile, one owner per PID; separate runtime folders,
  stop files, native capture/transport state and durable queues. Market/city is
  included in queue and API identity. Versioned LocalAppData runtime copies keep
  old restoration journals instead of deleting a potentially owned runtime.
- Read-only live-driver stress: two synthetic PIDs, eight simultaneous C# handles,
  4,000 alternating reads; no cross-PID bytes; closing handles leaves others valid.
  `tests/DriverConcurrency.Smoke` is repeatable. This is not a two-game movement
  test. Only one game is currently open.

No kernel driver, anticheat configuration or loaded game binary was replaced.
