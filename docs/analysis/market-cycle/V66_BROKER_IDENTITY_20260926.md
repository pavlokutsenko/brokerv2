# V66 broker identity diagnosis — 2026-09-26

Collection STOPPED by user. No automatic traversal is authorized until broker
is repaired. UI18684/game21404 were closed normally for early-reader tests. Rotation
was temporarily disabled through PROFILE → Rotate characters for finite
exclusive diagnostics; RESTORE original enabled/10±2 when testing ends.
No DB reset. No purchases. No second reader.

## Confirmed evidence

- V65 broker acd0f561:1922/1922 replies,1774 traders,77 unbound after C# join.
  Missing identities covered all3shop types; not evidence of77close/reopen events.
- PID2132 warm diagnostic:1930/1930,1773/1773 names.
- Restarted owned client PID18544/slot1: initially101actors/0shops;37s later
  4338actors/1772shops. Cold diagnostic1927/1927,1772/1772 names.
- Same PID moved via normal geometry to old center destination82055,147929.
  Broker1919/1919,1766traders,1765names. Actual missingTrader33 is present,
  sameOID1338059371/name butkiosk0. This proves an independent closure-filter
  race, NOT the root cause of mass77loss.
- Natural rotation while UI collection stopped replaced18544 with21404 at
  21:17:24. Failed finite18544 call was a stalePID rejection, not a new crash.
- New21404 broker1926/1926,1758traders,1682names:76unresolved.
  74are absent from persistent actor list. Other2areMyXaa/MuJIJIuoHep,
  knownnames withkiosk2(shopmanagement), excluded by active-shop filter.
  All107loadedlevels/7054actors stillfind onlythese2; no otherclass contains
  the74 at the knownObjectIDfield. CurrentPapaurposition81141,148192 is
  outside savedcenter. Moving to realcenter for samePID comparison now.
- Reflection only/read-only metadata: HUD hasGetCharacterByObjectID16bytes
  andGetActorNameByID24bytes; not invoked. Owner metadata inworkspace JSON.

## Current source/stage changes (not deployed yet)

- C#honorsfalse broker completeness: retries in place15s/max3, never enters
  Readingprices frompartial, safe partial upload/noabsentdeletion.
- Identity capture accepts guardedstandingtype0 only for broker-returnedIDs;
  type0cannotbindpricejob. It is not admitted as active radar shop.
- Current-PID guardedidentity snapshots before/duringevery5s/afterqueries.
  Final missingIDs get3bounded retries0.5/1/2s. No fixedbrokerinterval.
- Diagnostics savedrejectedIDs/reasons, absentIDs, unresolvedIDs andcounts.
  Summarynamed_traders now reflects finalbinding instead of onlybefore.
- 29BrokerWorker tests andModuleIsolation withnewpartialretry/closedidentity
  regressions passed. build.ps1 stage818filespassed.
  workspace/market-cycle-v66-stage. DO NOTdeclarefixed:76reproduced evenwith
  accumulation/retries. Continue root investigation and live verification.

Artifacts workspace/v66-*.json/log/py. Existing V65 installed, buffer-lifetime
fix stays. No automatic price traversal.

## Early reader test, 21:50–21:56

- PID21404 moved to center82041,148028; same76 missing from actors, including
  the same74 absent from the GameMode actor map. Center/waiting did not fix it.
- Existing CollectionCycle.LiveSmoke gained optional launch-early/broker-only
  mode. LaunchModule accepts a host callback before auto login; default launch
  still has no reader. Native login artifacts and in-memory DPAPI password
  decryption added to the harness. First two harness attempts failed before
  launch (missing harness dependencies), not client crashes.
- PID15128/Papaur: reader attached before login. 2052packetcharacters,
  1765visible shops. Broker4d9b4495:1925/1925,1766traders/8880rows,
  1765native names; C# packet join recovered the final identity. Full valid
  broker accepted for upload; zero price routes. Owned game closed afterwards.
  This alone does NOT reproduce/fix the massive missing74: native was nearly
  complete in this launch too.
- PID19524 launched without reader; one finite normal geometry route staged
  the old problematic spawn81154,148193. UI13652/game closed normally,10sec
  pause. PID16732 early-reader broker-only test now running. Collection is
  still globally stopped; rotation temporarily off. No database reset.
- Harness now waits for world character packets before enabling the finite
  cycle, preventing preview-capsule4/4 attempts during loading. Broker writes
  small .identity-join.json diagnostics with native/packet counts and exact
  packet-recovered/unbound IDs; no guessed or previous-PID identities.
- Latest29worker tests passed again. Further proof required before deployment.
