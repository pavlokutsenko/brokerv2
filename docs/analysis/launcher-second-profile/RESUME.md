# Second-profile agent readiness on another PC

2026-09-27: user reports "HWID: agent did not confirm readiness within
15 seconds" on the second Launcher profile on another computer. They explicitly
requested testing this computer first. No remote logs or hardware evidence yet;
do not claim that the remote fault is fixed or extend the timeout without cause.

Local reproduction uses the actual extracted Launcher ZIP 173904, BuildId
8dcaf722f2aa4e4ea4d879f6186f8b3f, checked against its .sha256. Launcher PID
23476 runs two saved Gamma profiles through --launch-profiles:
b1cec477-8755-4bbe-8990-49c905bf4d5f and
e84911d8-0abd-42c0-940a-874e15b085f3. Their own New template1/2 identities are
used; proxy is off, AutoLogin is off. The startup observer is diagnostic only,
with no login-account argument and no production changes or gate bypass.

17:42:10 local: first profile PID 23396 ready; second PID 756 reaches ready at
17:42:45 while first stays alive. Both native agent-PID.txt files say ready.
Second native lease has flags=1, required=1, controller=1, error=0, fresh
controller/agent heartbeat. Its longer total startup was in Waiting for game
window, before the 15-second agent confirmation phase. No readiness timeout.

Second PID 756 later exits with code 0; no protection error was recorded.
A repeat starts PID 24760 and reaches ready at 17:44:34; its agent file also
says ready. Later it returns to Client exited without a protection error.
Do not attribute these normal exits to the user's remote failure or infer
automatic recovery: AutoLogin was off. Launcher and first profile are left
available for the user; no test deadline closes them.

Evidence: workspace/launcher-soak/second-profile-01.log and LocalAppData/
PriceCheckCollector/logs/agent-{23396,756,24760}.txt. This verifies local agent
readiness with two clients, not remote hardware or full world/login/proxy
acceptance for those profiles. Existing unrelated Collector/native/package
changes from concurrent chats are preserved. No runtime fix or new ZIP is
justified by the local result alone.

Next evidence needed: the agent-*.txt modified during the failed second launch
on the other PC (this shared native log directory also serves Launcher), and
launch-guard-PID.txt if present. An absent agent file distinguishes failure to
start the DLL from an explicit initialization failure; ready plus no ready
event is a different path. Keep required guards enabled.
