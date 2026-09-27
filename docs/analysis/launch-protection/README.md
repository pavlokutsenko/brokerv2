# Live launch protection acceptance

2026-09-27. Test the shared production launch module against the installed
LU4 test client and saved Gamma account. Proxy credentials are provided only
through stdin; profiles, template identity and account settings are loaded
into memory and their files must remain unchanged.

Use the existing validated native agent, login functions and read-only radar.
Cheat Engine and Arduino are not involved. No collection worker, movement,
shop request, purchase or server upload runs during this test.

Acceptance: protected launch, real Windows HWID API readback, rewritten world
handshake, authenticated proxy traffic in both directions, continuous checks,
one character rotation with the collector reader, and route revocation that
stops the client and rejects another character action.

Runner: `tests/LaunchProtection.LiveSmoke`. Raw output belongs under
`workspace/launch-protection-live/`; native hardware/world/proxy traces are
under LocalAppData/PriceCheckCollector/logs. Do not copy credentials into docs.

Current status: shared production launch passed on the supplied proxy
(`attempt-12-standard.log`): slot 0 with reader, then slot 1 without reader
before login; Windows hook readbacks, world rewrite, continuous protection,
route-revocation termination and rejected next character gate all confirmed.
Production settings hashes remained unchanged. The final rebuilt binaries
passed again (`attempt-13-final-build.log`), including both slots, continuous
checks, revocation and rejected next character gate.

The earlier client closures after login are recorded in FAILURES.md. Their
exact trigger is unproven: every check passed when added individually, then
the unchanged full combination passed after restoring the normal agent DLL.
Do not claim one disabled check was the cause. See RESUME.md for the sequence.

Portable archives remain paused at the user's request. No new ZIP was created.
