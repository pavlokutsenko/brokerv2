# Initial live failure

Attempt 01, 2026-09-27 10:07 UTC: login preflight CONNECT succeeded. The real
root launcher attempted TCP DNS on port 53; the supplied HTTP proxy rejects
CONNECT to port 53 with HTTP 403, including public resolver addresses. Startup
stopped before the game window and production settings remained unchanged.
Proxy trace: LocalAppData/PriceCheckCollector/logs/proxy-tcp-20440.csv.

HTTPS CONNECT to 1.1.1.1:443 succeeds. Redirected TCP DNS is therefore adapted
to DNS over HTTPS (RFC 8484) through the same authenticated proxy, with normal
TLS certificate verification and no direct fallback. UDP/IPv6 remain denied.
This compatibility path still needs the real-client acceptance below.

Attempts 02-04: TCP DNS through HTTPS succeeded (two real replies), the game
window appeared, Windows API readback and supported active64 layout passed.
The initial strict envelope check was too early: the original encrypted
world identity is generated for the later world handshake. Pre-login now
checks the supported layout; the actual send must validate the envelope and
produce a different identity before forwarding its rewritten copy. The world
indicator stays pending until that send and real bidirectional traffic.

Attempt 03 also exposed an incorrect interpretation of the driver's denied
attempt counter. A blocked connection proves enforcement; it is displayed
separately and does not invalidate a still-active route. UDP/IPv6/raw and
other loopback endpoints remain denied. Actual route loss still terminates.

Attempt 04 (PID 14612) exited during account login, before a world handshake.
The protected login tunnel reached CONNECT 200 but recorded no login payload.
Add passive connect/bind/recv diagnostics that preserve Winsock errors and do
not replace the mandatory send hooks; inspect exact endpoints and exit code.
No settings file changed, and no live-success claim has been made.

## Attempts 05-10: account-login silence

The actual login destination observed by the in-process connect hook is
185.29.255.102:2108. An independent authenticated CONNECT probe receives its
98-byte binary greeting, both with and without Proxy-Connection: Keep-Alive.
The game tunnel gets HTTP 200 but neither pump observes login bytes before
the client exits, around 18 seconds after the login call. HTTPS:443 has real
bidirectional traffic. No world-HWID confirmation has been observed.

Disproved paths:

- A native getaddrinfo adapter saw no resolver calls and did not restore
  login. The experimental adapter was removed; do not repeat that path.
- Denying ordinary UDP socket binding breaks probing/fallback independently
  of egress enforcement. Normal TCP/UDP binds are now allowed; authorization
  still denies UDP/IPv6 egress. Tests check actual packet delivery because a
  successful UDP send return is not proof of delivery.
- Driver process metadata now reports lifetime and creation time, avoiding
  reliance on externally accessible handles. Attempt 09 still failed, with
  zero denied attempts and no kernel/Windows lifetime disagreement. This
  robustness change did not explain or fix the login failure.
- Attempt 10 attaches the reader before login, as the former collector did.
  It still fails; missing reader initialization is not the cause.

All production settings hashes remained unchanged. Raw attempts are under
`workspace/launch-protection-live/`. Diagnostics log lengths, socket metadata
and status only; credentials and login packets are not captured.
