# Four-profile live acceptance, 2026-09-28

The user's coordinated restart installed Collector build
`897beadf69064364945a9a3adf246109` at 20:13 Europe/Kiev. Collector PID
6016 started Gamma 8840, Black 15660, White 20172, and Carmine 8472. Each
client had its own PID and creation time, passed the protected world check,
attached a reader, and had collection enabled. Initial game windows occupied
top-left, top-right, bottom-left, and bottom-right, respectively. The
shared-module rotation/corner smoke test passed; the live session did not
reach a successful character rotation after deployment, so post-rotation
window placement remains for the next live check.

The first complete native center/broker passes were:

| Market | Native identities | Center open traders | Broker traders | Item replies |
| --- | ---: | ---: | ---: | ---: |
| Gamma | 1889 | 1620 | 1622 | 1807/1807 |
| Black | 186 | not inspected in UI | 118 | 319/319 |
| White | 62 | not inspected in UI | 32 | 118/118 |
| Carmine | 765 | not inspected in UI | 586 | 906/906 |

The new Gamma UI displayed 1620 center traders at 20:19:36, from the
confirmed native snapshot. The previous running build displayed about 218
because it counted only packet-visible radar shops. The 1620/1622 difference
is between current open-shop identities and broker traders, not a fall back
to the old partial radar count.

By 20:25, exact reads / individual current ACKs were Gamma 35/34 plus one
historical receipt, Black 6/6, and Carmine 171/171. White had no new exact
target on this interval but completed repeated broker passes. No new
`invalid_reply` or Collector error appeared during those passes. A read-only
check of all four market SQLite outboxes found them empty after the temporary
White broker queue drained. This confirms delivery progress on four separate
markets without equating a short-lived queue with failure.

At 20:26, Gamma's automatic client restart failed because the character
selection screen did not appear before its timeout. Gamma lost its process
and collection state. Collection was then stopped for Black, White, and
Carmine; those three games and the Collector remained running. The monitor
was paused after this session stop. The timeout's root cause is not yet
confirmed; do not classify it as a corner-placement failure or restart the
four profiles without a new user request.
