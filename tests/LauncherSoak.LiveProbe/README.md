# Actual Launcher soak observer

This test-only startup hook is loaded into the real portable
PriceCheck.Launcher.exe. Production App/MainWindow startup, profile loading,
ownership, UI refresh, protection and recovery run unchanged. The hook records
runtime state, ClientHealthProbe, native lease ages and visible journal events
every two seconds. It retains a process handle for the final exit code when allowed.
For a manual-login profile it uses an existing saved Collector account in memory
through ClientLoginService, preserving the Launcher's own template and PID.
No credentials are copied into Launcher settings or diagnostic logs.

Set PRICECHECK_LAUNCHER_SOAK_LOG to a workspace log path,
PRICECHECK_LAUNCHER_SOAK_PROFILE to a saved Launcher profile GUID,
PRICECHECK_LAUNCHER_SOAK_ACCOUNT to the authorized saved Collector account GUID,
and DOTNET_STARTUP_HOOKS to this built library. Launch the ordinary EXE with
--launch-profiles=<GUID>. The observer has no rotation, exit or fault timer;
the real Launcher owns the client throughout. Close Launcher normally to stop.
The hook is never distributed in portable packages.

Optional PRICECHECK_LAUNCHER_SOAK_LOGIN_ACCOUNT selects an authorized saved
Collector account for an isolation experiment during normal AutoLogin. The
hook changes only the service argument, preserving the saved Launcher profile,
its template, HWID, proxy, UI credentials and startup/after-world gates. Omit
this variable to test the exact saved Launcher account.
