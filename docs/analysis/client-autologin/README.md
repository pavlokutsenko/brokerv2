# Programmatic LU4 login (2026-09-23)

The Collector profile stores an opt-in account login, a game server and a character slot. The password is protected with Windows DPAPI for the current user in `%LOCALAPPDATA%\PriceCheckCollector\profiles.json`. The plain text password is never placed in the repository, launch arguments or diagnostic logs. A short lived PID-scoped memory mapping passes it to `PriceCheck.ClientLogin.dll` and is cleared after the native call. The native DLL is loaded through the existing GUI message hook after the LU4Memory-backed packet radar is installed.

## Verified path

- Client: `lu4.bin` SHA-256 `F3E792E5A79CBE1BBC92BF99EEAEA38DBA4304E48D065C828308940C94128D1D`, PE timestamp `0x956E0D97`, image size `0x0DCEB000`. Native code checks the latter two before touching the game structures. Other builds require new analysis and should fail closed.
- `NetLoginLibrary.ConnectToLoginServer(UserLogin, UserPassword)` was called through `ProcessEvent` on the game's GUI thread. The user observed the server screen.
- `LU4LoginMode.SelectGameServer(ServerID=1)` selected Gamma. The user observed the character screen, and the game connected to the Gamma world endpoint.
- `LU4LobbyHUD.SelectCharacter(Slot=0)` selected the first character. The user confirmed entry into the game world.
- A subsequent end-to-end smoke using the production `ClientLoginService` and release native DLL launched the protected client, installed the identity agent and radar, then completed all three native calls. The client and radar were still active 15 seconds later; the smoke stopped only its own client.

The other game servers' internal IDs and character positions beyond slot zero have not been verified. The profile UI currently offers Gamma and numbered slots. A native call returning success proves that the game function was invoked; it is not a server-side acknowledgment of successful world entry. The 30-second server-selection timeout makes fast sequencing necessary.

## Native boundary

`client_login.cpp` uses the verified build's GObjects and FName/UE function layout, resolves live login-mode and lobby-HUD objects by class ancestry, and validates function indices and parameter sizes. The game thread receives one login call, then the server call, then the character call. Missing transient game objects are retried within bounded windows. Unknown build, missing function, invalid slot and expired stage report separate negative status codes. The application removes the GUI hook after status 4; the native module pins itself before calling `ProcessEvent` so it cannot unload under its callback.

The stage does not solve the authenticated HTTP proxy failure in protected LU4. That limitation and its evidence are in `../driver-agent-load/PROGRESS.md`.

The server-selection delay in the native login sequence was increased from 2.5 to 5 seconds after the pipe transport added latency. Direct end-to-end login still passed after this change. The native statuses for Gamma and character mean that the client function was invoked, not that the server accepted a world session; the smoke additionally checks that the driver-backed radar remains live 15 seconds after the calls. Current proxy-specific failure analysis is in `../proxy-routing/README.md`.
