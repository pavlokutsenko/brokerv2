# Preserve exact radar checks when broker confirms them

2026-09-26 user steering: a radar-read shop becoming broker-known must remain
checked unless a real new change requires another visit.

Confirmed unnecessary repeated visits in V50: JabaTrader, TopraIII and
CASH4RESOURCES had identical exact item sets/type/position before and after
the next broker. Server compared new broker rows with its older broker stock,
ignoring the newer exact radar snapshot. Broker stock must remain separate;
copying prices into it or overwriting exact rows is not the fix.

API patch15:42:42 local: cycle-price-coverage.policy.ts suppresses only
NEW_LISTINGS already covered by a current exact snapshot read since the
previous broker, with type/position/physical row count/24-hour guards.
Completed queue revision and original deadline remain unchanged. Real new
items, increased uncovered counts, movement/type changes/reopening still queue.
Also validated the V50 assigned-reopen payload with queueLease+radar admission;
server schema now allows that validated combination, while stale tokens cannot
acknowledge foreign jobs. No collector restart was needed for this API patch.

7pure policy +4isolated PostgreSQL integration tests, API typecheck/build passed.
Server detail: C:/broker-server/docs/RADAR_BROKER_CONFIRMATION_20260926.md.

Live baseline16current/COMPLETED shops saved in
workspace/v50-before-broker-confirmation.json. Follow next full broker and
compare job revision/status/deadline; c33c02129f7245aba4c957bf86f3a806 ongoing.
V50 already completed3whole cycles, centers at15:35:53/15:38:36/15:41:49,
each followed immediately by broker. Later cycles have occasional bounded
geometry retries; do not claim all geometry resolved from the price/queue fix.

## Live confirmation15:49:04 local

All16baseline current exact prices remained COMPLETED, with identical queue
revision and24-hour available_at_utc through the next full broker. Evidence:
C:/broker/workspace/v50-before-broker-confirmation.json and
C:/broker/workspace/v50-after-broker-confirmation.json. This verifies the server
coverage policy in the real market. A separate runtime old-OID binding defect
(JabaTrader/TopraIII already read under newOID) caused false REOPENED; V51
AcceptExact updates binding after durable spool, preserving true later reopen.
