# Runtime loader

These files are copied into `DriverRuntime` by the collector publish. The WPF
bootstrapper first tries to open `\\.\LU4Memory`; only a missing device starts
the PowerShell loader with UAC. The loader verifies every binary hash, captures
the current DSE value, loads the verified candidate5 service, and restores the
exact captured value in `finally`.

The KDU components are the same provider-1 loader files used by the recorded
live driver validation. Do not replace them without updating both the hashes
in `load-driver.ps1` and the live validation notes.
