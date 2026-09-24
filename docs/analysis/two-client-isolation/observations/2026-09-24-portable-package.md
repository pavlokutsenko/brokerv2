# Portable package for a second physical PC, 2026-09-24

The user requested an archive to test whether the current build works beyond
the original computer. A clean publish was produced with root `build.ps1`
into a separate workspace directory; BrokerWorker was current and reused.
The active release, Collector, drivers and both game processes were left running.

Artifact: `release/packages/PriceCheckCollector-win-x64-20260924-132255.zip`.
Size **84,846,409 bytes (80.92 MiB)**. SHA-256:

`BA2C36536B0B9D76508FB54F1FF6C41D5CDAC2AF81A6D2C680393E34BF32403D`.

The ZIP contains a `PriceCheckCollector` folder, 563 hashed files and
`package-manifest.json` (564 total files). A matching `.zip.sha256` sidecar is
stored next to it. `scripts/package-portable.ps1` reproduces clean publication
and packaging; it refuses recognized settings/state in the input and removes
PDB/Python caches from the copied runtime. It does not package the working
release directory or LocalAppData. No profile, credentials, proxy settings,
collected database, game files, memory dump or raw packet capture is included.

Included helpers from `tools/Portable`:

- `README-FIRST.txt`: Russian setup instructions and matched solo/paired
  controls using fixed settings, followed by a several-hour paired interval.
- `Verify-Package.ps1`: all-file size/SHA-256 verification, no driver access.
- `Start-WithDiagnostics.ps1`: verifies files, obtains administrator context,
  enables existing metadata logging and starts Collector before games. Rejects
  an already running Collector or game session. Removes inherited research
  test flags and normalizes module paths for the Windows PowerShell 5.1 loader.
- `Collect-Diagnostics.ps1`: copies a fixed allowlist of metadata logs, records
  OS/build and relevant process IDs/start times plus binary hashes, then creates
  a support ZIP under LocalAppData. It does not modify or stop game sessions.

## Validation on the original PC

Fresh root build passed. The final ZIP was independently extracted into an
unused folder, and all 563 manifest entries verified using Windows PowerShell
5.1.19041.7725. All four packaged PowerShell scripts parsed under that version;
the changed final launcher was rechecked after module-path normalization.
BrokerWorker `--help` succeeded from the extracted runtime with only Windows
directories on PATH and empty PYTHONHOME/PYTHONPATH. No absolute development
drive paths were found in bundled BrokerRuntime Python helpers.

Critical extracted binary hashes matched the active tested release:

| File | SHA-256 |
| --- | --- |
| Collector DLL | `34EE87006E5F34F0265582FBBBB8AC82B07E478DAADDDFA31ACE58399EDB3467` |
| ClientAgent DLL | `457D621DF542FB07CACC8E02856EF28BBB835A9864A422B3785461057A899E95` |
| ClientLogin DLL | `69348E8236BB56F516425F2D9445184CBB2CE7DFE67269F6B989D51195250772` |
| LU4Memory SYS | `C7228FD5D285C29268A64707B3062FEEEFCCB76BEB5332CB8BF5B4E52CCC64DA` |
| BrokerWorker EXE | `A7B31075FA9CF14DFB9C62ACE7BCE1EA4AEE6D8BC0B56EE4EAADE0D2D303FFC6` |

A diagnostic-export smoke ran under Windows PowerShell 5.1 with synthetic
LocalAppData outside the repository. Its ZIP contained exactly the expected
proxy metadata, environment report and manifest; synthetic profiles and raw
capture markers were excluded. An initial test child inherited PowerShell 7's
module paths and lacked Get-FileHash. Retesting with a fresh Windows PowerShell
environment passed; the launcher now explicitly supplies Windows PowerShell
module paths before elevation/Collector startup. This was test/environment
configuration, not a game or driver failure.

At **10:23:38 UTC** the original 24-hour monitor remained running with
290/290 healthy samples, no gaps and only its baseline event. Both original
game PIDs and monitor PID remained alive. No second Collector or game was
launched during package validation.

## Remaining validation

The other physical PC has not run this package yet. Runtime completeness and
matching local binaries do not prove driver-loader compatibility with another
Windows/security configuration, or same-world success on different hardware.
The documented next test is solo for each profile, then the pair, with fixed
inputs and recorded duration. Existing DPAPI passwords cannot be transferred
by copying this computer's settings. Account reuse while still logged in on
the first PC is a separate confounder and must be avoided in a comparable test.
Version guards and the 4096-event IOCTL trace cap still apply.
