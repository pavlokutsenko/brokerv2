# New accounts and late world disconnect — 2026-09-24

User resumed the task with two new accounts and authorized replacing the
Gamma/Black profile credentials. User reports another request after the two
world entries, followed by disconnect. Its protocol meaning is not known.

Credentials are stored only in LocalAppData, password protected using the
existing CurrentUser DPAPI format. Previous encrypted profile configuration
is backed up outside the repository under
`PriceCheckCollector/research/late-world-disconnect/`.

Initial state rediscovered: no game processes; desktop Collector PID 21148.
`LU4Memory` and `PRProt` running, no `LU4Probe`. Release agent SHA-256 remains
`457D621DF542FB07CACC8E02856EF28BBB835A9864A422B3785461057A899E95`.
No driver reload is needed. Hardware templates/profile IDs remain unchanged
so the new accounts can be evaluated with the previously tested launch path.

Plan: sustained solo and paired controls, recording per-process live TCP,
world state, packet lengths/directions/timing and existing IOCTL metadata.
World coordinates can remain after disconnect and are not sufficient alone.
Raw data and credentials must not enter the repository.

## New Black solo, current release

PID 20172, desktop Collector 20552, 08:54 UTC: no other game was running.
The new account passed login-server exchange, then world exchange was again
client 15 / server 13 / client 69 / upstream EOF after about 2.7 seconds.
`world_identity_applied detail=16` is present; relevant IOCTLs succeeded.
This is a failed **solo** control, before any late challenge. It does not
establish an account ban, an identity ban, or the earlier late-disconnect cause.
The monitor was stopped after the known early EOF; no paired trial with this
account is interpretable until the solo failure is understood.

Metadata logs are outside repo in `logs/*-20172.csv`; the duration monitor
directory is `research/builder-source/duration-solo-20260924-115412`.

Gamma solo PID 336 at 08:56 UTC likewise closed upstream after 15/13/69.
User then supplied another authenticated proxy account and authorized fresh
HWIDs. Both templates now use that same proxy account. Proxy-only Black solo
PID 5048 received CONNECT 200 but again upstream EOF after 15/13/69 at
08:58:14 UTC. This route change alone was insufficient.

## Regeneration semantics

Found a configuration defect: regenerating the 20 template HWID values did
not change the world identity, which was derived only from the durable
profile ID. New `LaunchIdentity.WorldIdentitySeed` now participates in the
derived launch identity. The profile ID remains unchanged; an empty seed
preserves old fixed-template behavior. Generated identities get a new seed,
and the template editor preserves it when copying/saving.

The resolver hashes profile GUID bytes plus seed and takes 16 bytes, so
sharing a template does not merge profile identities. Root release build,
resolver checks (stability, separation, rotation, legacy and invalid input),
and template editor save/clone smoke passed. Native agent is unchanged.
Both assigned templates were regenerated once and fixed for matching tests.
This implementation fixes rotation semantics; it does not by itself prove
that a server restriction caused the earlier failures.

Fresh fixed HWIDs + user-supplied proxy: Black PID 6980 entered Gamma and
returned valid coordinates with a live connection at 09:01:22 UTC. User also
observed that HWID change worked and requested reverting the proxy. This
short successful run was deliberately ended at about 09:02 UTC; it is not
evidence of survival past the late-disconnect interval.

Only the five proxy fields were restored from the encrypted original
template backup, preserving the new identity values and profile IDs. Started
matched Black solo with original route at 09:02 UTC; late stability pending.

## Passive post-entry traffic, Black PID 11152

With new fixed identities and the restored original proxy, world entry
passed at 09:02:52 UTC. After initialization sends of 141, 3, 7 and 7 bytes,
the client sends 27 bytes approximately every 30 seconds. Upper `send` and
post-anticheat lower `send` both report 27. Each matches a successful
59-byte-input IOCTL `0x222160`, caller clmods64 RVA `0xB68F`, through
clmods64 `0x162E261` / `0x1633D45` and game `0x16CB803` / `0x4C22A0B`.
This is unlike the initial 37-to-69 expansion. It does not prove that these
messages contain no identity or establish their protocol meaning. Receiving
world traffic continues through successful `0x22215C` calls.

User requested saving complete regeneration in Collector and investigating
post-entry anticheat exchange. The released configuration change regenerates
all 20 supported template HWID fields plus the world seed. Local comparison
confirmed all 20 changed; original proxy credentials were checked by decrypted
equality without printing them (DPAPI ciphertext naturally changes on save).
Collector DLL SHA-256:
`34EE87006E5F34F0265582FBBBB8AC82B07E478DAADDDFA31ACE58399EDB3467`.

Black solo PID 11152 passed **420 seconds after readiness**, continuously
sampling alive process, coordinates and established TCP. World started at
09:02:49 UTC; after completion at about 09:10 UTC the connection was checked
again and remained established. No upstream EOF occurred. This is a valid
long solo control on the restored original proxy and new fixed HWIDs.
Samples: `research/builder-source/duration-solo-20260924-120218/`.

Next started a paired control with the exact same saved configurations,
Gamma first and Black second, target hold 600 seconds after both ready.

## Paired periodic-message snapshots

Current pair: Gamma PID 19660 and Black PID 14980, Collector 18620. Both
entered Gamma with original proxies and the same fixed identities as solo.
Read-only record snapshots bracketed one 27-byte send in each PID, uniquely
matching the active64 record by PID at `+0x60` and checking record identity
before/after. Files remain under LocalAppData
`research/late-world-disconnect/periodic-14980-121306` and
`periodic-19660-121627`.

Using record RC4 state at `+0x240CC`, processing the 25-byte body reproduces
the complete captured post-state exactly in both cases. Inverting the keyed
XOR with pre-key at `+0x2460E` yields 27-byte messages with byte 2 equal to
`0x03`; the key counter advances by exactly 25. Neither decoded message
contains the original envelope middle16 or the per-profile replacement16
as a contiguous byte sequence. This does **not** exclude derived, partial or
hashed identifiers, nor establish the meaning of opcode 03.

Limits: the existing exact replay on the earlier 37/69 fixture still passes
and locates the initial inverse key at `+0x245FE` and final key at `+0x2460E`.
An attempted full 27-byte helper replay reaches an unmapped read at driver
RVA `0x1354D3`; the captured context is insufficient for that branch. Do not
describe it as full periodic-handler emulation. No live writes, additional
hooks or driver loading were used for these periodic snapshots.

A second Black snapshot `periodic-14980-121803` independently reproduces
the RC4 transition and +25 key-counter step. Between Black captures, the
little-endian DWORD at decoded offset 3 advanced by 300562 over 300559.6 ms
of observed send timestamps. This supports a millisecond time-counter field;
its exact source is not established. Other DWORD fields also change, so do
not label the entire message as only a timestamp or invent their meanings.

## Completed long paired control

The exact saved configurations passed **602 seconds** after both clients
ready: 09:11:49.162–09:21:51.512 UTC, 116 consecutive valid samples.
Both PID 19660 and PID 14980 retained coordinates and established TCP;
Collector 18620 had four established connections. Recheck after the monitor
exited still found both game connections alive. Both upstream logs have zero
world EOF and continue receiving traffic and sending periodic 27-byte data.
Clients were left open. Samples and copied proxy metadata are in
`research/builder-source/duration-paired-20260924-121037`.

Thus the old late disconnect did not recur within this ten-minute paired
control, following the seven-minute matched solo. No further late-message
rewrite was needed in this interval. The old failure reason remains unknown;
do not extrapolate to unlimited stability or identify a specific banned
field from simultaneous regeneration of 20 HWIDs plus the world seed.
