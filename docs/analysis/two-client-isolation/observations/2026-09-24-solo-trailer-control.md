# Solo control for the clmods driver-global copy

The earlier concurrent test changed the eight-byte `clmods64.dll` data slot
at RVA `0x7EFA2` in the second client and still failed to enter Gamma. That
negative result was ambiguous: changing the slot might itself break the
anticheat proof. This control used the saved Black account, fixed launch
template, authenticated HTTP proxy, root-only early SMBIOS/adapter
replacements and programmatic Gamma/character selection in both conditions.

The original Gamma process was stopped temporarily. An unmodified solo Black
run entered Gamma and the radar reported the player position; its test
launcher then exceeded an outdated 150-second wait because it still used an
older DLL with a 120-second post-login hold. The test-owned orphan client was
stopped and ordinary Gamma was restored. The LiveSmoke win-x64 build was then
updated to use a five-second post-login hold.

With Gamma absent again, Black PID 10756 had its `clmods64.dll` slot changed
to a random eight-byte value before login. The new value's SHA-256 prefix was
`a29d487638295b41`; the original driver-global prefix was
`ba5f276b9f1bd915`. Readback confirmed the change. The proxy returned HTTP
CONNECT 200 for the world connection. After the 15-byte request, 13-byte
reply and 69-byte follow-up, the upstream server sent the next **193-byte
reply**. Programmatic character selection succeeded and the radar reported a
player position. The test exited normally. Thus this user-mode slot change is
**tolerated in a successful solo world entry**. It does not prove the changed
slot is part of the server proof; the original driver global and other copies
remained unchanged.

Ordinary Gamma was restored as PID 4480 and held its proxy tunnel. The same
recorded eight-byte replacement was applied before login to concurrent Black
PID 20164. The proxy logger appends by PID, so the harness counted existing
rows before release and checked only newly appended rows; this avoids a false
positive when Windows reuses a PID. Readback confirmed the replacement was
still present **after the new 69-byte world follow-up**. Early firmware and
adapter replacement counters were positive in both the solo and concurrent
traces. Gamma's first-client tunnel remained established. The second client's
proxy returned CONNECT 200, then logged 15 client bytes, 13 server bytes, 69
client bytes and upstream EOF without a 193-byte reply or character list.
The test-owned second client exited; Gamma remained alive.

These matched controls show that replacing this observed `clmods64.dll`
copy **alone** is not sufficient to get two clients into Gamma. A successful
solo run rules out the simple explanation that this copy edit always breaks
login, but it does not rule out a driver-side copy, a signed early response,
other hardware signals or a server rule.

## Matched early-response plus user-mode-copy control

The temporary root-only `DeviceIoControl` callback was rebuilt to replace
only the final eight bytes of a successful full `0x4BA`-byte `0x222044`
response in the test child, after calling the real driver. It used **the same
recorded eight bytes** as the `clmods64.dll` slot edit above. The original
driver global, the first client and ordinary release launches were not
changed. With Gamma absent, solo Black PID 19496 showed one full early
response replacement, one early firmware replacement and one early adapter
replacement in its root trace. The matching clmods slot persisted through the
new 69-byte world follow-up. Its proxy returned CONNECT 200, then the server
sent the 193-byte reply; character selection and radar position succeeded.

Gamma was restored as PID 20600 with an established Collector proxy tunnel.
Concurrent Black PID 19724 used the **same recorded bytes** for both the
early `0x222044` trailer and clmods slot. Its root trace again recorded one
early response replacement and active firmware/adapter replacements. Its
clmods slot still held the replacement after a newly logged 69-byte world
follow-up. The proxy returned CONNECT 200 but logged upstream EOF before the
193-byte reply; no character list appeared. The first Gamma tunnel stayed
established and the test-owned Black client exited.

The early response trailer plus its observed user-mode copy are therefore
**tolerated in a successful solo session but insufficient together** for a
second Gamma session. This controls for the simple concern that editing that
response necessarily invalidates the proof. It does not show whether another
driver-side copy, a derived signature or an unrelated identifier causes the
remote close. The temporary early-response mutation branch was removed and
the native/test win-x64 builds passed again.

Raw traces and the eight-byte replacement record are ignored under
`workspace/two-client-isolation/`; neither the value nor credentials are
committed. The current native agent has no `0x222044` mutation branch. The
normal Collector and restored Gamma use the published release DLLs.
