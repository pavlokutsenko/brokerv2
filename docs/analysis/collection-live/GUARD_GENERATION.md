# Launcher generation guard — 2026-09-27

The resumed Gamma run used Collector17912, game24132 and launcher root10672.
At14:59:34 UTC it failed the root route check: active=false, capabilities31,
owner0/17912, relay0/58544. The game was terminated. The broker had received
all1846 responses but one trader ObjectID1231113113 had no live identity;
therefore the enriched inventory remained incomplete. No center-absence
retirement was authorized and the immediate price section could not complete.

The monitor used a generic PID liveness function for the old launcher root.
That function accepts any live process with the same number, even after Windows
reuses the PID. The original launcher's route legitimately disappears on exit;
a replacement process then makes the old code report a fatal lost route.
No exact birth sample of PID10672 at the failure was retained, so PID reuse is
a supported mechanism, not a proven attribution of this particular exit.

ClientLaunchGuard now captures root and game ClientSession identities and
checks both PID and creation time before route monitoring. A changed root
generation is treated as departed. The current game still needs its own route,
owner, listener, HWID and fresh heartbeat. No driver or network policy was
relaxed. Bind also captures the game generation before publishing its PID.

ReusedRootChecks deterministically models a reused root with raw PID liveness
still true and its old route gone. The protected game survives; removing its
own route then causes a fatal guard failure. Existing exit/query-race, natural
exit, continuous failure, kernel traffic-denial and agent-readiness checks are
retained. Initial new-fixture setup lacked an inherited child route; the fixture
now explicitly installs its owned synthetic game route before binding.

Root build.ps1 succeeded with BuildId b72f6334c0a24727b3775c1b54b68198, preserving
concurrent launch readiness/timeout edits. Canonical packages are18:06:24 and
18:06:30. The next live run must validate progress beyond the previous failure;
do not claim completion of the center reconciliation or new corner approaches
from this incomplete run.
