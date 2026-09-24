# HardShift proxy path: live LU4 trace

## Result (2026-09-23)

HardShift 0.1.0 successfully launched the protected LU4 client with a per-client generated hardware profile and an authenticated HTTP CONNECT proxy. Programmatic account login, Gamma selection, first-character selection, and world entry succeeded in the resulting `lu4.bin`. The test proxy was a local relay that forwarded to the real login and world servers. This is an end-to-end test of HardShift's proxy path, not of the supplied external proxy.

The crucial difference from the Collector's failing pipe experiment is **where traffic is redirected**. HardShift routes the game's TCP connection to a listener owned by HardShift while leaving the game's ordinary socket receive path intact. The Collector's earlier pipe experiment replaced the game-facing `recv` return with raw tunnel bytes and bypassed an existing game receive transformation; see `../proxy-routing/README.md`. The current WFP implementation is documented in `../wfp-proxy/README.md`.

## Direct observations

- `HardShift.exe` SHA-256 `F631E48004AA087C3C970484E2FC9573A55DD17940F814C8A946531C98FD167F`; `HsAgent64.dll` SHA-256 `C6C18CC4A67C9F002D087D7DD7F2639DEE19E8822463B6BEF02A5ED2778CF692`. No HardShift binary was copied into the product.
- A separate temporary template used the UI's `HTTPS` proxy type, `127.0.0.1:15099`, and dummy Basic credentials. The external listener received **plaintext** `CONNECT ... HTTP/1.1` with valid Basic authentication, so this UI type used ordinary HTTP CONNECT in this test; it did not establish TLS to the proxy.
- HardShift's diagnostic log records root `lu4-win64-shipping.exe` PID 19176 at 17:05:25, child `lu4.bin` PID 1408 at 17:05:28, per-PID profile application, physical SMBIOS patch verification, and `HostProxy register pid=1408 ... type=1 auth=1 valid=1`.
- At 17:05:28 HardShift opened listener `127.0.0.1:59437` and logged `RdrCb pid=1408 hostListener=59437 ownerPid=10364 ... ok=1`. The host listener belongs to HardShift PID 10364.
- A `netsh wfp show state` capture after the test contains HardShift's registered callout and filter at **`FWPM_LAYER_ALE_CONNECT_REDIRECT_V4`**, provider `HardShift Redirect Provider`, sublayer `HardShift Redirect Sublayer` (weight 65532), and `FWP_ACTION_CALLOUT_UNKNOWN`. The filter has no WFP conditions, while `RdrCb` registered a specific PID, indicating that the callback performs its own process selection. The state capture is `workspace/hardshift-wfp-after.xml` (ignored). The callout remained registered after the GUI closed; this test did not remove third-party WFP objects.
- During world entry, Windows reported established TCP sockets `lu4.bin:59463 -> 127.0.0.1:59437` (HardShift), `HardShift:59464 -> 127.0.0.1:15099` (test proxy), and `test proxy:59465 -> external-address-3:7782` (world server). The game PID did not own the upstream or external proxy socket in this observed chain.
- The test proxy accepted authenticated CONNECT for login `external-address-2:2108` and world `external-address-3:7782`. The first world request was 15 bytes; the first 13-byte wire reply matched the privately compared prefix of the reply that stalled the Collector's pipe route (packet bytes omitted). The HardShift client went on to enter the world; the screenshot is `workspace/hardshift-game-current.png` and the test proxy log is `workspace/hardshift-proxy-test/proxy.log` (both ignored local artifacts). The world tunnel sent at least 4,228 bytes before the test client was stopped.
- The programmatic login used the existing `ClientLoginService` and `PriceCheck.ClientLogin.dll` against HardShift's owned PID. No game UI typing was used. The service completed account, Gamma and character calls; the game screenshot independently confirmed world entry.

## Interpretation and remaining boundary

The WFP state proves that HardShift registered an IPv4 ALE connect redirect callout. The runtime `RdrCb` log and PID-owned loopback sockets show this callout was used for the protected client in the successful proxy run. The callback's exact code, redirect context and method for recovering the original destination are **not yet proven**. HardShift's `HsAgent64.dll` is also mapped in the child (see `../hardshift-launch/README.md`); the trace does not prove whether that DLL participates in destination metadata or proxying. It is incorrect to reduce this to a `version.dll` trick or to a top-level Winsock `recv` shim.

At the time of this trace, the Collector's LU4Memory driver had no WFP imports or redirect lifecycle. The later implementation added a PID-scoped connection redirect, a host listener that owns the external CONNECT socket, preservation of each original destination, loop prevention and cleanup on process exit. The driver-backed radar/packet hook remains independently bound to the owned final PID. The Collector's own local-proxy world test and external-proxy traffic evidence are in `../wfp-proxy/README.md`.

## Cleanup

The traced game instance was stopped through HardShift, then HardShift was closed. The local proxy was stopped. All three original HardShift client/template files matched the pre-test backup by SHA-256 after restoration. The temporary template was moved out of `%APPDATA%\HardShift\templates` to `workspace/hardshift-config-before-proxy/`; no client remains bound to it. No LU4 or HardShift process was left running.
