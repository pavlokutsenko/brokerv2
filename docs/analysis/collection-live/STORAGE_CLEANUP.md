# Project artifacts and local cache retention — 2026-09-27

User explicitly requested deleting project junk and preventing accumulation,
then asked whether the collector database is cleaned.

Initial disk free space was0. The project directory apparent total included
42.28GiB through workspace/second-game-root, a junction to the real installed
game C:\Games\Lu4-Clear-test\Liberta Ultimata4. Its contents were not deleted.
Old publish trees, package staging copies, installer fixtures and redundant
recovery versions were the actual large storage consumers. Source, configuration,
market data and small diagnostic reports are retained. No other client stopped.
Final audit: project3.28GiB excluding the game junction; C: free123.17GiB.
workspace0.55GiB, release0.93GiB, test build caches1.01GiB, src0.60GiB.
Old compiled copies were removed while small reports/configuration were retained.
One protected old driver SYS could not be removed and was left intact.

Build publication and ZIP packaging now delete their own temporary trees in
finally, including failed builds. Desktop package smoke also cleans extracted
packages. Recovery pruning preserves the current committed manifest and its
backup, plus versions younger than10 minutes to avoid a concurrent writer.
Current canonical recovery is valid. The old foreign backup referring to a
deleted isolated build was removed; a future publication will retain this
current version as its predecessor. Publication no longer copies nested
.package-state journals from a source layout into the destination. Tests verify
current/previous manifest preservation and rejection of an out-of-root target.
Old generated publish/package folders can be cleaned with
scripts/cleanup-publish-temp.ps1; it skips live executable directories and links.
All cleanup validates resolved paths and removes individual files/empty folders.
An initial recursive Remove-Item was rejected by automatic review; the narrower
file/empty-folder cleanup succeeded, followed by explicit user cleanup authority.

Read-only Gamma audit:2530 durable trader identities,916 snapshots,0 queued
operations,1304 route rows; database7655424 bytes, WAL6826872 bytes before restart,
quick_check=ok. No snapshots were older than7 days. Delivered outbox entries
already were deleted after ACK; superseded snapshots previously had no retention.

LocalCycleStore now performs bounded maintenance at startup and hourly when
captures arrive. It expires delivered superseded snapshots older than7 days,
preserves all pending payloads, each trader's newest snapshot and current read
anchor, and never deletes durable identities/history or undelivered events.
At most2000 candidates are considered per run. Passive WAL checkpoint and8MiB
retained journal limit avoid a blocking VACUUM; freed DB pages are reused.
The smoke test checks expiry, pending/current/latest/recent preservation and
the hourly throttle. The production audit is read-only; cleanup is owned by
the application store. Server immutable price history remains intact.
