# Market cycle checkpoint

Latest: user rebooted after reporting a zero-counter startup. Progress and
bidirectional connection-cache reuse fixed and packaged; collector/server/site
reopened. User will log in manually. See STARTUP_PROGRESS.md; live validation
of the updated progress UI remains pending on the new game PID.

2026-09-25. Implementation, WPF UI, v2 server integration, packaging and focused
checks completed. See IMPLEMENTATION.md for exact evidence and limitations.
The old sector route is no longer called by RefreshAsync. User-saved centers
were preserved; no unbounded game run is active.

Live packaged test: center return 11.125 s; broker 1,718 traders / 8,997 rows,
1,706 bindings (partial authority); price tour 24 sell shops / 142 exact lots in
41.2 s, zero recoveries, 25 server cancels/replies. Hooks restored, target zero.
Driver: 2 synthetic PIDs / 8 handles / 4,000 reads passed. Coordinator: two
simulated independent markets and stop isolation passed. Two actual moving
clients and long unattended operation have not been verified.

Prepared control upload is C:/broker/workspace/cycle-live/review-outbox.
Automatic approval review rejected starting that upload, reason only "blocked
by policy". Nothing from this control export has been sent. A user approval
question is pending; do not work around that decision. API migration is applied,
API/web health is good, isolated PostgreSQL integration tests pass.

Next: approved live upload/site verification; two-game live concurrency and
long-run buffer reuse validation. Exact buy/package decoding and other city
maps remain separate unproven work. Unknown client builds fail guarded checks.
