# Ruled-out assumptions

- **HardShift uses the Collector's `version.dll` staging path:** a previous live child mapped `HsAgent64.dll` from HardShift's assets; no game-adjacent `version.dll` appeared. The new proxy trace confirms a separate host listener and driver callback.
- **The game's original socket can directly connect to a loopback or external proxy from the protected process:** Collector tests returned Winsock 10060. HardShift instead connects the game to its own local listener through the registered redirection path.
- **Replacing top-level `recv` with bytes from a host pipe is equivalent to HardShift's path:** the Collector returned raw the encrypted reply prefix (bytes omitted) and stalled. HardShift's external proxy saw the same raw first world reply, while the game entered the world.
