# LU4Memory startup bootstrap

The portable collector owns driver startup. `App.OnStartup` opens
`\\.\LU4Memory` before creating the main window. If the device is missing, the
collector runs the bundled `DriverRuntime\load-driver.ps1` through UAC and
waits for completion. A failed or cancelled load stops the collector, so LU4
cannot be launched in an invalid order.

The published runtime contains the verified candidate5 binary plus the pinned
KDU provider-1 files. The loader validates all SHA-256 hashes, updates a stopped
service path when the application has moved, temporarily changes DSE, and
restores the exact original DSE value in `finally`. It does not unload the
driver when the collector closes. The diagnostic log is written to
`%LOCALAPPDATA%\PriceCheck Collector\logs\driver-bootstrap.log`.

Validation:

1. With `LU4Memory` already running, the collector must open without UAC.
2. After a clean reboot, starting the collector must show one UAC prompt, load
   `LU4Memory`, and then open the main window.
3. Cancelling UAC or corrupting a runtime file must show a startup error and
   leave the game-launch UI unavailable.
