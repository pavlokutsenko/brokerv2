# Launch protection smoke

Run from C:\broker in PowerShell after build.ps1:

    $dotnet = 'C:\tools\dev\.tools\dotnet\dotnet.exe'
    & $dotnet run --project tests/LaunchProtection.Smoke/LaunchProtection.Smoke.csproj -c Release
    & .\tests\LaunchProtection.Smoke\test-native.ps1

Managed phase tests inject fake process/login functions and never start LU4.
Native tests compile the production lease/login/send gate and watchdog into a
benign executable, with fake identity/layout functions. They verify that an
invalid lease, HWID or world layout terminates that executable.

For real kernel policy and controller-monitor checks, use elevated PowerShell
with the verified LU4Memory loaded:

    & $dotnet run --project tests/LaunchProtection.Smoke/LaunchProtection.Smoke.csproj -c Release -- --wfp
    & $dotnet run --project tests/ProxyRedirect.Smoke -c Release
    & $dotnet run --project tests/DriverConcurrency.Smoke -c Release

The WFP tests use local TCP/UDP endpoints, TEST-NET destination 198.51.100.99,
synthetic child processes and an authenticated local HTTP proxy fixture.
They assert non-delivery of direct traffic, inherited denial, fail-closed
revocation/controller exit, suspended startup, continuous failure termination,
and independent world generations even when send() precedes CONNECT 200.
They do not use production proxy credentials, game login or user configuration.
Driver build/promotion remains manual; see native/LU4Memory/README.md.

The UI fixtures in Launcher.Smoke and CharacterRotation.UiSmoke check both
hosts' bindings to the shared HWID/proxy indicators. Live game acceptance
remains separate: [LAUNCH_PROTECTION.md](../../docs/LAUNCH_PROTECTION.md).
