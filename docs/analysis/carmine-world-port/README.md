# Carmine world traffic verification

On 2026-09-28, the fourth Collector profile (Carmine) reached the LU4 game
world but failed the protected launch check after 120 seconds. The existing
native world-send hook and managed proxy health check both recognized only
world ports 7782, 9971, and 9972. A metadata-only launch trace for a fresh
Carmine client showed the world connection on port 9973. No packet bodies,
addresses, proxy credentials, or identity values were needed for diagnosis.

Both port classifiers now include the observed 9973:

- `native/ClientLaunch/world_send_probe.cpp` gates world identity application.
- `src/PriceCheck.Launching/Services/ProxyTcpBroker.Health.cs` recognizes
  proxied world traffic for the launch guard.

The native DLL rebuilt successfully. A separate live Carmine launch with the
saved template and proxy entered the world, reported hardware and proxied
world traffic ready, and passed the protected-world check. The diagnostic
stopped only its own client and confirmed production settings were unchanged.
The three Collector-owned profiles continued running throughout this test.

Build `897beadf69064364945a9a3adf246109` was installed in the live Collector
on 2026-09-28. Carmine PID 8472 (created 20:15:37 Europe/Kiev) passed the
same protection in the Collector, attached its reader, and began collection
at 20:16:49. Its first complete broker pass counted 586 traders with 906/906
item replies. By 20:25, 171 exact reads had 171 individual current server
ACKs, with no invalid replies or Collector errors; its read-only market
outbox was empty. This confirms the fourth profile progressed beyond login
and sent verified prices. Keep the port list based on observed server traffic;
do not relax the launch guard to infer success from account-login traffic alone.
