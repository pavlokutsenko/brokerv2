# Gamma world HWID handshake after client update

On 2026-09-28, the protected Gamma launch reached the game world, but the
Collector still reported "HWID verified, waiting for login" and rejected the
world proof after 120 seconds. The failure repeated with a fresh Collector and
game process. A metadata-only port trace confirmed the expected Gamma world
port (7782), so the port classifier was not the cause.

An opt-in native category trace for a third launch showed one 35-byte upper
world send and one 67-byte lower world send. The existing native gate accepted
only the older 37 -> 69 form, so the world identity rewrite never ran. No
packet contents, proxy credentials, or identity values are recorded here.

`world_send_probe.cpp` now recognizes both observed source/wire length pairs
and requires the lower length to equal the upper length plus 32. The rewrite
still requires the supported image-backed buffer, its matching length header,
the current active64 envelope, and a replacement identity different from the
original. The 16-byte identity and eight-byte tail are addressed from the end
of either supported wire layout; an unsupported shape fails closed. The driver
and the managed protection requirement were not relaxed.

The native rebuild and `LaunchProtection.Smoke --wfp` passed. `build.ps1`
published and packaged build `c04da3e08c6d4cc9a8704a5ddb060f8b` as
`release/packages/PriceCheckCollector-win-x64-20260928-232524.zip`. The
Collector launched from that release at 23:25:42 Europe/Kiev (PID 19864),
launched only Gamma (initial game PID 17048), confirmed the world identity
application, passed the protected-world gate, attached the reader, and started
collection at 23:26:30. It reached the saved center and started the broker
pass at 23:27:17. These PIDs are historical observations, not stable targets.

At 23:29:11 the first center capture was complete with 2,042 native identities;
the broker returned 1,789 traders and 1,896/1,896 item replies. The first
price pool admitted 639 targets (cap 958). By 23:32, 29 exact shop reads had
29 individual server acceptances, no invalid replies or delivery retries,
and the read-only Gamma SQLite outbox was empty. The saved Gamma trader pause
is 5 seconds, not the default 30 seconds. The route can admit newly observed
targets within its cap, so the initial 639 is not a final visit count.

One complete finite price pass, including exact reads and delivery ACKs,
remains to be observed. Do not equate world entry or broker start with that
full acceptance. The live Collector and Gamma are left running.

## Post-rotation navigation failure

At 23:48-23:49 Europe/Kiev, Gamma naturally rotated from game PID 17048 to
18528. The new client passed world HWID confirmation and collection resumed.
The original route ID `b0231a576f1f45169986698d8c5c4372` continued with
600 remaining targets from the new character position; the pool was not reset.

From 23:50:07 through at least 23:51:36, five consecutive price sections
failed before an exact shop read: `wait_navigation_capsule` observed an
effective capsule of `(7.5, 23.0)`, which is not among the native-confirmed
profiles in `capsule_profile.py`. The repeated failures requeued sections and
produced many "No complete valid shop capture" warnings. The first client had
reached 121 exact reads; that count did not advance during the five failures.
The Gamma outbox remained empty. This is a navigation-safety block, not a
repeat of the HWID failure. Do not expand the accepted capsule set without
native validation of this character's geometry and movement clearance. The
running Collector and game were not modified or restarted by the monitor.

At 00:07-00:09 on 29 September, the next natural rotation launched Gamma
game PID 10124. The same finite route ID was retained, but this character
reported a different unconfirmed effective capsule, `(7.5, 24.0)`. Three
successive navigation sections failed before a new exact read; individual
server acceptances remained at 117. The read-only market outbox contained one
unattempted state entry, not a delivery retry. This broadens the navigation
validation issue beyond the previous `(7.5, 23.0)` character. No process or
configuration was changed by the monitor.

At 00:26 the next natural rotation launched Gamma game PID 21908. It resumed
the original route without a capsule rejection. By 00:29, individual server
acceptances had risen from 117 to at least 124, with no new errors; the
read-only market outbox was empty. This is a recovery for the current
character, not evidence that the two unconfirmed capsule profiles are fixed.
The finite pass is still incomplete.

The 7.5x23 capsule was later validated on a live three-account Gamma run and
admitted to the navigation profile; see
[`gamma-multi-account-20260929`](../gamma-multi-account-20260929/README.md).
The distinct 7.5x24 profile remains unvalidated and blocked.

## First finite pass result

At 00:37:37 on 29 September, the live Gamma log recorded `finite price pass
complete` for the first resumed pool and explicitly began returning to the
center for the next broker. Subsequent center and broker events confirm the
transition. Before that completion entry, the log recorded 160 individual
server acceptances; the initial broker had returned 1,789 traders and
1,896/1,896 item replies. This confirms the finite route lifecycle, not
successful exact reads for every initially admitted target: two intervening
characters repeatedly failed capsule validation. The next broker cycle has
already begun. Gamma collection remains enabled and both Collector and game
are still running. The read-only SQLite outbox showed one unattempted price
entry during the transition, with no delivery retry or post-completion error.
