# Resume: remote prices

## Production fix delivered

User requested the fix after the research controls. The guarded relay resolver
is now in the working lu4_target_controller.py through send_prologue.py.
Packaged prepare/price passed on MHE: fresh three-row shop, 1.38s, target 0 after
cleanup. Both running installations received the two sender scripts and passed
their own install/status/uninstall checks. Native ProcessEvent selection
remains a failed path; the working collector uses its existing direct sender.
See observations/003-production-send-fix.md for delivery and rollback.
Statements below that production was unchanged describe the earlier phase.

## Latest continuation checkpoint

2026-09-24: read **observations/002-send-controls.md** first. The automated
positive control now works through a research-only adapter; the send-path
uncertainty below was narrowed to the existing intervening send chain.

Manual/HUD/controller actions use the same thread5588, connection and native
socket method. Upper send reports20/20 for programmatic 0F, but the lower-send
callback never sees it. Broker and cancel pass. The prior send relay points to
`clmods64.dll+0xB700`; its exact discrimination condition was not determined.
The outer agent MinHook chain explains why the old direct installer refused
its signature. The isolated adapter validates this chain and then the original
retained-prologue guard; production code and deployed DLLs remain unchanged.

Direct NoO control returned a fresh three-row A1 without movement, with server
selection. Direct ABAPKA control returned all nine exact prices after selection
and a379.516-unit approach. The prior remote (item,quantity,enchant) multiset
matches all nine. This is **not** the remote/no-target solution. Observed final
actor-center distance109.37 also means95 is a conservative collection limit,
not a proved exact server boundary.

Empty-array83 near/far reached the lower callback and returned no new shop
list after a valid positive control. No nonempty purchase/sale array was sent.
2,223 earlier RX packets/108,205bytes contained no matching ABAPKA lotObjectIDs
as32bitLE or prices as32/64bitLE. This does not exclude other encodings, unseen
requests or native caches. A bounded native reflected market-name search over
GObjects0..25999 found no additional normal sell-shop list-read request.

**Direct TX coverage:** use `requests[].payload` as well as native tx[];
direct injections bypass the native encrypt-entry observer. tx=[] does not
mean no action was sent. Encrypted lower-send and server RX are also saved.

All seven research sites restored/read back: direct/post, lower-send callback,
send return, ProcessEvent, RX/encrypt. Prior radar/network detours preserved;
allocations retained until process exit. At cleanup target0, position
(15386.438784315807,143136.1474320044,-2691.482319368166), near ABAPKA.
Later read-only observation found a different position/target; do not clear it
or assume the cleanup position is still current. No research state remains.

User subsequently proposed coordinate spoofing, noted that the server owns
coordinates, then explicitly deferred that investigation and asked why target
selection failed. **No coordinate-spoofing test was performed.** Do not resume
it automatically. If research is requested again, start from the new controls,
not the superseded failed native calls. Runtime files remain under the same
LocalAppData research directory for PID19660; refresh pointers after restart.

## Earlier checkpoint (superseded where noted above)

2026-09-24. **Unsolved:** exact prices without server target selection, movement
and normal interaction have not been obtained. Working route untouched.
Read README, observations/001-wire-and-controls.md and FAILURES.md.

Strong evidence: PID 19660, PE 0x956E0D97/0xDCEB000. Native broker pass gave
353 rows/102 traders in 153 requests, no drops, constant position, null target,
no outgoing 0F/0C. All FE ED 01 rows are fully accounted for as 16-byte
ObjectID/amount/currency records. ABAPKA (~501 units) yielded nine rows and +0
enchant with baseline rechecks; prices null.

User manually opened NoO: two A1 replies, three rows, quantities 1/2/4 and prices
989894747/1150000/135000. Raw A1 uses 64-bit quantity/price; native conversion
at RVAs 4CC31B0/4CC31F6 narrows to 32 bits before ProcessEvent. Diagnostic
shop_wire.py preserves high bits, tested offline. No live overflowing value
was available. Buy/package/new-shop wire variants remain unvalidated.

Main unresolved control: ClientSelectTarget invoked through the ProcessEvent
bridge emits the same 18-byte plaintext as manual interaction but receives no
new shop response/initial target ACK. A repeat after the successful manual open
also gave no fresh A1. Plaintext at encrypt entry alone does not prove delivery
or server acceptance. Native broker requests have matching responses.

The old direct sender refused the current ws2_32!send E9 relay layout and
rolled back before installing either entry patch. Do not weaken its guard.

**Next:** passively compare actual send path, return values, thread/caller
context for one manual action and one programmatic action. Establish a positive
automated near-shop control before judging alternate read requests. Preserve
the current hook chain. Test a specific serializer hypothesis; no opcode fuzzing.
Bounded reflected-actor inspection found no price array; native caches remain
an open path, not disproved by this inspection.

All three research patches (RX, encrypt, ProcessEvent) were restored/verified;
original radar detour preserved. No target/direct-send/suppression hook left.
Client 19660 was responsive, selected target zero. Final position:
(15354.20500742538, 142758.00908745977, -2693.716201663832).
The user moved slightly in the manual control; only the automated broker passes
may be described as stationary. Collector PIDs 18620/20348 were left running.

Private evidence: LocalAppData/PriceCheckCollector/research/remote-prices/19660.
packet-capture-restored.json and shop-capture-restored.json retain rollback
evidence. Rediscover PID and regenerate actor/UE/route state after a restart.
