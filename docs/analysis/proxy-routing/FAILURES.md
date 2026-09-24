# Ruled-out proxy paths in current protected LU4 build

- Game-process direct connect to the HTTP proxy and game-process loopback relay: Winsock 10060 before tunnel setup. Fresh test sockets inside the same game process gave the same result, while the original game endpoint connected.
- External helper `WSADuplicateSocket` into protected LU4: Winsock 10024; same helper succeeded into Collector.
- Top-level `recv`/`send` replacement backed by a PID-scoped pipe: both HTTP CONNECT tunnels opened and bytes crossed, but world entry stopped after the first raw 13-byte reply. The direct client's `recv` sees a transformed header.
- Hooking the original `ws2_32!recv+5` alone: passive direct-run hook did not see the first world reply. The pre-existing `clmods64.dll` handler does not use that simple path for the observed read.
- Early root/child DLL injection: both remote loads succeeded, but the child exited before its game window. The failure reproduced with no HWID/proxy hooks; a suspended-root control without injection stayed alive at 45 seconds. See `../early-agent/README.md`.
