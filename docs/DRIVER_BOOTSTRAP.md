# LU4Memory startup bootstrap

The portable collector owns driver startup and runs with the Windows
`requireAdministrator` execution level because the verified driver device is
administrator-only. `App.OnStartup` opens
`\\.\LU4Memory` before creating the main window. If the device is missing, the
collector runs the bundled `runtime\DriverRuntime\load-driver.ps1` with the same
elevated token and
waits for completion. A failed or cancelled load stops the collector, so LU4
cannot be launched in an invalid order.

The published runtime contains the verified candidate5 binary plus the pinned
KDU provider-1 files. The loader validates all SHA-256 hashes, updates a stopped
service path when the application has moved, temporarily changes DSE, and
restores the exact original DSE value in `finally`. It does not unload the
driver when the collector closes. The diagnostic log is written to
`%LOCALAPPDATA%\PriceCheckCollector\logs\driver-bootstrap.log`.

The loader script is stored as UTF-8 with BOM. This is required because the
portable app deliberately uses the Windows PowerShell 5.1 available on clean
Windows installations; a BOM-less UTF-8 script containing Cyrillic diagnostics
is otherwise decoded as the legacy ANSI code page and fails during parsing.
Arguments are passed with `ProcessStartInfo.ArgumentList`, and output is
captured when the collector already has an elevated token.

The current portable root contains only its EXE and runtime directory.
AppContext.BaseDirectory points to runtime. The saved LU4Memory service path
was updated to `C:\broker\release\PriceCheckCollector\runtime\DriverRuntime\lu4_memory_wfp.sys`
after comparing the driver hash with the tested bytes; the running driver was
not reloaded during this packaging change. Current protection checks and
capabilities: [LAUNCH_PROTECTION.md](LAUNCH_PROTECTION.md).

Validation:

1. With `LU4Memory` already running, the collector must open without UAC.
2. After a clean reboot, starting the collector must show one UAC prompt, load
   `LU4Memory`, and then open the main window.
3. Cancelling UAC or corrupting a runtime file must show a startup error and
   leave the game-launch UI unavailable.

Live validation on 2026-09-23 after moving the application to `C:\broker`:

- Windows PowerShell 5.1 parsed the bundled loader without errors.
- `LU4Memory` reached `RUNNING` and the service path was updated to
  `C:\broker\release\PriceCheckCollector\DriverRuntime\lu4_memory.sys`.
- Independent IOCTL verification opened `\\.\LU4Memory`, resolved the running
  collector image base and read bytes `4D 5A` (`MZ`) from that process.
