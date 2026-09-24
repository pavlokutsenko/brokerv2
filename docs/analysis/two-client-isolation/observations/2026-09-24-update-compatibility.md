# Compatibility with future client updates — source audit, 2026-09-24

The user asked whether a client update and changed offsets would leave the
collector working. **That is not guaranteed.** The current release was validated
against a specific LU4/anticheat combination; it does not automatically discover
and adopt every changed offset or protocol layout. This entry is a source audit,
not a test against a newly updated game.

## Existing protection and remaining coupling

- `native/ClientLaunch/client_login.cpp` checks client PE timestamp and image
  size before account login, and validates selected object/function metadata.
  GObjects RVA, UObject indices and ProcessEvent slot are compiled constants.
  A mismatching PE version returns status -3, exposed as an unsupported-build
  error by `ClientLoginService`.
- `Runtime/Radar/LocalPlayerPositionReader.cs` checks the same client version
  before using its compiled GWorld RVA and pointer-chain field offsets.
  A different version has no coordinate profile and is rejected. Finite value
  and pointer checks do not discover a replacement layout.
- Radar's receive hook searches for one matching byte signature, then checks
  original bytes or a recognized existing patch. Relocation of unchanged code
  can be tolerated; changed signatures, instruction semantics, packet formats
  or field layouts still require validation.
- Broker helpers discover some Unreal globals and validate structural
  candidates, but still assume object/field layouts. Dynamic discovery of some
  addresses is not universal adaptation to a game update.
- `native/ClientLaunch/world_identity.cpp` checks active64 PE timestamp/image
  size, selected instruction bytes, envelope integrity, buffer location and
  69-byte layout. Its driver and clmods RVAs remain compiled constants.
  Rejected rewriting returns a send error on the recognized 37-to-69 path.

The last protection must not be described as a global fail-closed gate:
`world_send_probe.cpp` only requests rewriting on its recognized send path.
Changed message lengths or a different send route can bypass that recognition;
hook installation also returns success without the lower hook if the expected
send-entry jump is absent. In such a case, rejecting an unsupported envelope
inside `RewriteWorldIdentity` does not provide complete coverage. This audit
does not establish what any particular future update will do.

Ordinary module-base relocation is handled by calculating relative addresses
from the current module base. Changed RVAs, field layouts, function indices
and protocol contents are separate compatibility changes. PE timestamp/image
size checks are not a cryptographic full-content identity check. Package file
hashes validate the delivered collector files, not a future game update.

## What can be promised

Saved profiles, templates and generated identities are independent settings,
but their presence does not ensure that native integration still works.
Auto login, coordinates, collection or paired same-world entry can stop working
after an update. Some mismatches already produce clear errors; the whole
application does not yet have a single compatibility gate covering all modules.

Before claiming support for a changed build, revalidate the affected version
profiles/layouts and repeat matched solo and paired controls. A future shared
compatibility check could block unsupported integrations before installation,
but it has not been implemented by this audit. The current running experiment
and portable package were not changed in response to this question.
