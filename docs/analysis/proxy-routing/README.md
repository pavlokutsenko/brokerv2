# Protected LU4 proxy routing (2026-09-23)

This file records the historical named-pipe `recv` experiment that stalled on the first world reply. Later, a live HardShift trace established a WFP connection redirect, and the Collector's independent PID-scoped WFP route passed local LU4 world entry and external-proxy world traffic. See `../hardshift-proxy/README.md` and `../wfp-proxy/README.md` for current evidence.

Goal: send one profile-owned LU4 process through an authenticated HTTP CONNECT proxy. Test credentials are held in memory only. Raw captures are under ignored `workspace/`; diagnostic CSVs are under `%LOCALAPPDATA%\PriceCheckCollector\logs`.

## Confirmed baseline

- Direct programmatic account login, Gamma, first character and driver-backed radar passed repeatedly. Two TCP destinations were observed: login port 2108, then Gamma world port 7782. The observed path used `connect`, `send`, `recv` and `select`; no UDP or `WSASend`/`WSARecv` calls appeared in the sampled interval.
- An independent helper received HTTP CONNECT 200 from the supplied external proxy. The local mock HTTP CONNECT proxy also returned 200 and forwarded bytes without modifying payloads.
- Inside protected LU4, a socket to the original login endpoint connected, but fresh sockets to loopback and the external proxy returned Winsock 10060. A direct game-socket redirection and an in-process loopback relay both failed before HTTP CONNECT. An external `WSADuplicateSocket` transfer into LU4 failed with 10024; the same helper succeeded against the unprotected Collector process. The exact restriction mechanism remains unconfirmed.

## Named-pipe transport experiment

`ProxyPipeBroker` in the Collector owns the external HTTP CONNECT socket and authenticates it. The agent associates each game socket with a PID-scoped named pipe, forwarding `send`/`recv` bytes without a direct socket fallback. This avoids the blocked destination at the game socket, but **does not complete world entry**. Both the supplied external proxy and a local mock reached the login server and world server; character selection timed out after the first world reply.

The first 15-byte world request was identical at the game API in direct and local-proxy runs. PktMon captures for both runs show the server's first 13-byte reply starts with the same four wire bytes the encrypted reply prefix (bytes omitted). Through a direct game socket, the game-facing `recv` returned a decoded header starting a decoded reply header (bytes omitted) and then sent the next 37-byte request. Through the pipe, `recv` returned the raw wire header, and the game sent no next request. The broker's recorded first word matches the raw wire capture, so the pipe did not corrupt that reply. These observations isolate the gap to an existing receive transformation in the client path that our top-level `recv` replacement bypasses. They do not establish whether that transformation runs entirely in `clmods64.dll` or also uses a driver.

Before our agent installs MinHook, the protected client's `ws2_32!recv` and `send` entry points already contain `E9` detours into private executable memory. The gateway branches to handlers in `clmods64.dll` and has another branch to the original Winsock body. In a direct run, a passive hook at `send+5` observed the 15-byte world request; a passive hook at `recv+5` saw no 13-byte world reply, while the top-level `recv` hook saw the decoded reply. This disproves the simple plan to move both proxy hooks to the original Winsock body at `+5`.

Opt-in trace of direct `GetAdaptersAddresses` calls confirmed that our separate HWID hook modified two returned adapter MACs per successful call. See `../hwid-surface/README.md` for scope and limits.

## Current status and next step

The production proxy mode is not verified for protected LU4. Keep experimental pipe routing out of the release path until the existing receive transformation is preserved. Inspect the `clmods64.dll` receive handler's call path with bounded diagnostics, then intercept the lower data source it uses; verify the decoded 13-byte reply, subsequent 37-byte send, character selection and world entry through the local mock before retesting the supplied proxy. Do not infer that the remote proxy is faulty from the present timeout.

An early root/child DLL load was also tried because an agent installed before the existing Winsock detours might preserve their order. It loaded successfully, but the final client exited before its stable game window even with no HWID or proxy hooks. See `../early-agent/README.md`. Normal Collector launches now reject a proxy-enabled template with an explicit message; the failing pipe transport is reachable only with `PRICECHECK_EXPERIMENTAL_PROXY=1` during controlled tests.

See [RESUME.md](RESUME.md) for exact code paths and captures; see [FAILURES.md](FAILURES.md) for ruled-out approaches.
