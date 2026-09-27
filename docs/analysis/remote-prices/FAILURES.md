# Failed paths and remaining hypotheses

- Hidden local target was historically still server target selection. It was
  not retried or promoted as a no-target solution.
- Price hidden in trailing broker bytes: disproved for all 150 observed search
  replies by exact framing and matched NoO quantity/price control.
- Native ClientSelectTarget through ProcessEvent: **local send-chain discrepancy**.
  Two nearby actions with bLockMovement 1, two with 0, and one after manual
  opening emitted 18-byte 0F plaintext but no fresh shop reply. Initial actions
  produced no target ACK. Follow-up: upper native send reports 20/20, but the
  lower-send callback never sees this action. Controller SelectTarget has the
  same outcome. Isolated direct sender now passes the near positive control.
- Native ClientTargetCancel emitted `11 00` and local target became null;
  this is different from old direct sender's `48` packet. Do not conflate them.
- Empty-array ClientPlayerShopBuy: serializer 4BD2330 emits
  `83 <ObjectID:i32> <count:i32>`; zero count contains no transaction rows.
  ABAPKA (~501) and NoO (~89) produced no fresh shop list. Near run included
  system message `62 5F010000 00000000` (ID 351), text not verified. Because
  normal automated action control also failed, that initial negative was limited.
  Follow-up direct-empty runs now have confirmed lower-send passage and a working
  near-shop positive control; still no fresh price list near or far. No
  purchase/sale row was submitted. See observations/002-send-controls.md.
- Old direct-send installer refused the current send E9 relay layout, freed
  its allocation and applied neither direct/post patch. Keep its guard. The
  research-only adapter now validates the outer agent MinHook chain and applies
  the same guard to the retained prior relay; production remains unchanged.
- Instrument setup first requested >64KiB allocation and hit WinError 87;
  driver per-allocation limit is 64KiB. No hook installed in that attempt; one
  unused 4KiB cave remains until client exit. Fixed by bounded separate slots.

Active hypotheses: an alternate specific protocol request, or native/unreflected
local cache. Neither confirmed nor globally disproved. Automated positive
control is now established. Bounded reflected native request inspection found
no extra sell-shop read request. Previously opened cache is not fresh remote
access. No opcode brute force was attempted.
