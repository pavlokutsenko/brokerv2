# Send-chain control and ABAPKA reference, 2026-09-24

PID 19660, same validated PE. User authorized target selection and the second
action's approach/open behavior as controls. No purchases or sale rows were
submitted. Production routes, profiles and deployed DLLs were unchanged.

## Why native selection failed

`send_probe.py` observes immediately after the native socket virtual call at
RVA 4C22A0B. Manual and ProcessEvent actions use thread 5588, the same connection,
socket object and virtual method (RVA 16CB7E0). Both return success and report
20/20 bytes. The controller Blueprint `SelectTarget(Actor, ForceAttack=false)`
also reports Selection=true but receives no target acknowledgement.

The current ws2_32!send chain is nested:

1. Entry E9 -> MinHook absolute relay -> ClientAgent hooked_send (+28C70).
2. Agent real_send (+FDE18) -> MinHook trampoline -> pre-existing relay.
3. Prior relay retains `48 89 5C 24 08`, then jumps to send+5.
4. send+5 E9 -> agent lower-send callback (+27E30) -> original body.

Read-only module-range validation identifies the prior relay's intercepted
send destination as `clmods64.dll+0xB700`. The exact acceptance condition was
not reverse-engineered; do not label a specific call-stack/TLS test as proven.

`winsock_capture.py` copies arguments at the existing lower callback and
forwards unchanged. In `lower-send-native.json`, the native action's 20-byte
packet is absent, while cancellation (4), broker (9) and heartbeat (27) appear.
This localizes the discrepancy to the intervening send chain. It does not
identify the precise filter condition or prove the OS accepted a packet merely
because the upper function returned success.

The old installer correctly rejected the outer MinHook relay. An isolated
`direct_control.py` adapter follows the reviewed agent trampoline and applies
the original retained-prologue guard to the prior relay. It uses the existing
direct sender/crypto command unchanged; it does not modify production code or
disable the installed network hooks. All state is in this PID's research dir.

## Positive control and no-target hypothesis

| Run | Result |
|---|---|
| direct-NoO | Two recorded 0F actions, one fresh A1, all three known prices. Target becomes NoO; position constant. Cleanup 48 acknowledged by 1F. |
| direct-empty-ABAPKA | Initially 488.79 units away, target null. `83 <ObjectID> <count=0>` reaches lower send, no fresh A1. RX includes `FE A0 00 00000000`; interpretation not established. |
| direct-empty-NoO | 86.98 units away, target null. Same empty-array shape reaches lower send, no fresh A1; system message ID 351 again. |
| direct-ABAPKA-approach | Two 0F actions, target ACK and approach, fresh 777-byte A1 with nine lots. Cleanup 48 acknowledged. |

The empty-array hypothesis now has a working automated positive control and
lower-send evidence. Its tested forms do not return prices. This does not rule
out every different request or target/shop type.

**Capture coverage:** direct injections bypass the native encrypt-entry TX
observer. Their exact plaintext is recorded in `requests[].payload`, with
lower-send encrypted observations and server RX. `tx=[]` in an approach capture
does **not** mean no action was sent. Always combine both action inventories.

## ABAPKA full-price reference

Start distance 488.7868; displacement 379.5163 units. Final actor-center distance
109.3685. This control should not be represented as proving a strict 95-unit
center-to-center bound: 95 remains the existing collector's conservative limit.
There was no explicit outgoing 0C in this window, but ordinary actions induced
actual server-driven movement. Thus the no-movement criterion fails regardless.

| Item ID | Quantity | Enchant | Exact wire price |
|---:|---:|---:|---:|
| 64 | 1 | 0 | 400000 |
| 6573 | 1 | 0 | 4700000 |
| 6576 | 10 | 0 | 125000 |
| 1458 | 5224 | 0 | 310 |
| 9910 | 157 | 0 | 165000 |
| 71 | 1 | 0 | 2400000 |
| 6575 | 1 | 0 | 950000 |
| 951 | 1 | 0 | 750000 |
| 6572 | 1 | 0 | 5200000 |

The multiset (item, quantity, enchant) equals the prior remote broker/enchant
result exactly. Raw A1 count=9 and length=21+9*84; no drops or truncation.

`compare_price_evidence.py` searched six earlier capture windows: 2,223 RX
packets / 108,205 bytes. None contained these nine lot ObjectIDs as i32, or
prices as i32/i64 LE. This is bounded evidence only: not continuous history,
not a heap scan, and not exclusion of other encodings/requests. Source windows:
broker-sweep-1, remote-ABAPKA-enchant, manual-shop-reference,
manual-send-control, direct-empty-ABAPKA, direct-empty-NoO.

Native reflected market-name inspection across GObjects indices 0..25999
found no additional normal sell-shop list-read request. Existing candidates
are own-shop management, purchase/sale, craft info, broker and incoming list
events. Native/unreflected code and dynamically loaded Blueprint paths are
not excluded by this bounded inspection (`native-market-surface.json`).

## Cleanup

Restored and read back all seven entry sites: direct/post, lower-send callback,
send-return, ProcessEvent, RX and encrypt. Preserved the prior radar and network
detours. Retained allocations until process exit to avoid freeing live code.
Client remained responsive, target zero, at
(15386.438784315807, 143136.1474320044, -2691.482319368166), near ABAPKA.
This was the cleanup state; subsequent user activity changed position/target.
No UI target-pointer masking, purchases, deployment, profile edits or production
sender changes. Artifacts: LocalAppData/PriceCheckCollector/research/remote-prices/19660.
