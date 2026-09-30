# Collector identity live observer

Read-only startup hook for a real portable Collector launched with its ordinary
`--launch-accounts=Gamma` argument. It records the configured account runtimes,
process birth, reader readiness, per-template identity tag, hardware/world/proxy
confirmation, OS health and the current native lease. It neither starts accounts
itself nor replaces login, templates, guards, readers, recovery or saved settings.

Build with the bundled dotnet:

```powershell
C:\tools\dev\.tools\dotnet\dotnet.exe build tests\CollectorIdentity.LiveProbe\CollectorIdentity.LiveProbe.csproj -c Release
```

Pass the built DLL through `DOTNET_STARTUP_HOOKS` only in the Collector's process
environment, together with `PRICECHECK_IDENTITY_OBSERVER_LOG` (workspace log path)
and `PRICECHECK_IDENTITY_OBSERVER_PROFILE` (saved root profile GUID), plus
`PRICECHECK_IDENTITY_OBSERVER_ACCOUNTS` (expected count; defaults to three). The ordinary
application performs login using saved credentials; the observer does not read or
record those credentials. Never add this hook to a distributed package.

PASS requires the expected number of distinct live PIDs and identity tags, the tag matching each
account/template, applied world bytes matching the expected identity, flags=7,
no native error, current process birth, fresh agent/controller heartbeats, connected
readers and responsive windows for sixty continuous seconds. Proxy readiness is
required only for accounts whose saved template enables it. At PASS the observer
renders the actual Collector window to a PNG, then ends while the app stays open.

The normal native hardware confirmation verifies SMBIOS UUID, MachineGuid, volume
serial and MAC through public APIs. This observer also verifies the confirmed
current world payload. It does not establish coverage of every hardware query,
other anti-cheat processes or a new driver which has not been loaded.
