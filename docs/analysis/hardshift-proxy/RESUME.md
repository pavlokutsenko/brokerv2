# Resume

Strongest confirmed lead: HardShift's working proxy launch connects `lu4.bin` to a listener owned by HardShift, which then opens authenticated HTTP CONNECT to the proxy. This preserves the LU4 receive transform. Evidence, timestamps and local captures are in `README.md`.

Next reverse step: trace the **game-to-HardShift listener** connection at creation and establish how the original destination is communicated. `netsh wfp show state` confirmed `FWPM_LAYER_ALE_CONNECT_REDIRECT_V4` with an unrestricted filter, but the callback body and redirect context are unknown. Compare a direct game socket's original destination with the redirected socket, then inspect the host's WFP redirect query or custom IOCTL path. Keep raw captures under ignored `workspace/`.

The Collector's independent PID-scoped WFP redirect and host TCP relay were implemented and passed benign socket, local LU4 world-entry, and supplied external-proxy traffic tests. See `../wfp-proxy/README.md`. The named-pipe `recv` replacement remains a failed historical transport. HardShift's exact callback body is still unconfirmed.
