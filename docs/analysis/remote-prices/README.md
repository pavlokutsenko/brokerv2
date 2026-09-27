# Remote trader prices

Started 2026-09-24. Scope: obtain every listing of a distant trader with exact
price, quantity and enchant, without a server target-selection action, movement
command, or ordinary shop interaction. A hidden local target pointer is not a
successful no-target result. Keep the working collector route unchanged.

## Evidence baseline

| Field / property | Remote availability | Evidence and limits |
|---|---|---|
| Market item IDs | Yes, broker | `broker_query.send_market`: request `21 <store_type:u8>`; `BrokerMarketItemsList` returns int32 IDs. |
| Item-to-trader membership | Yes, broker | `20 <item_id:i32le> <store_type:u8> <enchant_min:u8>`; `BrokerTradersByItem` rows include trader ObjectID. |
| Sell/buy/package role | Query context | Store types 1, 3, 8; not an independently decoded price field. |
| Quantity (`amount`) | Yes, broker | Reflected row size 0x18; `<i4xi4xq>` decodes ObjectID at +0, currency ID at +8, amount at +0x10. Raw wire row is16bytes. Matched against all three NoO and nine ABAPKA shop rows. |
| Nickname / durable traderKey | Join from actor/radar | `collect_full_broker_inventory.loaded_trader_names` joins ObjectID to actor snapshot. Broker row itself does not carry nickname; ObjectID is session/shop-generation identity. |
| Duplicate listings | Preserved by broker | `BROKER_ENCHANT.md`: one trader occurs five times in threshold-zero result. Must retain multiset multiplicity. |
| Exact enchant | Indirect, demonstrated | `BROKER_ENCHANT.md`: item 34932, thresholds 0..5 give 14,8,8,8,8,0 rows (six +0, eight +4). No explicit enchant in broker rows. Live listing changes can invalidate threshold differences. Bulk inventory script currently queries only threshold zero. |
| Exact unit price | Not available through current broker decoder | Only ObjectID, nested ID and amount in captured reflected row. This proves absence from that decoded structure, not absence from every raw response or alternate request. |
| Exact shop rows | Yes after normal interaction, range constrained | `read_open_shop.decode_row`: 0x38-byte rows; item ID +4, count +8, enchant +0x14, price +0x2C, buy count +0x30, base price +0x34. `event_shop_cycle` sends RequestAction twice; documented validated radius is 95 units. |
| All current lots of one trader | Reconstructable membership, no atomicity proof | Full market enumeration plus per-item query and ObjectID filter can gather membership. Check every response, truncation and relevant store types; moving listings during a pass remain a limitation. Exact-price completeness is unproven remotely. |
| No server target selection for prices | Not achieved | `no_ui_target_shop_cycle` waits for target ACK, hides controller+0x898 locally, then sends another action. `event_shop_cycle` also sends two actions. Neither meets the criterion. |

## Current session

Initial process discovery: one LU4 process, PID 19660. Two Collector processes
were present (18620 and 20348); their roles/ownership must be checked before any
game command. No CE process was listed. Arduino is not involved.

First step: inspect current PID-bound state, validate read access, and trace
normal broker requests/replies. Compare raw packet fields and client conversion
code before forming a new request hypothesis. No speculative opcode fuzzing.

## Live result, 2026-09-24

**Success criterion not met:** no fresh exact price of a distant trader was
obtained without server target selection/ordinary interaction.

| Question | New evidence |
|---|---|
| Hidden price bytes in broker replies? | All 150 search replies fit `10 + 16*N` bytes exactly. All 353 rows contain ObjectID, amount, currency ID, with no trailing bytes. |
| Is amount actually price? | No for matched NoO control: amounts 1, 2, 4 equal shop quantities, while prices are 989894747, 1150000, 135000. |
| Remote collection without targeting/movement? | 153 requests, 102 trader IDs, constant position, null target, no outgoing 0F action or 0C movement. |
| One distant trader? | ABAPKA (1291927129), initially ~501 units: nine observed rows, quantities and +0 enchant, with threshold-zero rechecks. Prices remain null. Sequential observation, not atomic snapshot. |
| Confirmed source of exact price? | Normal A1 sell-shop reply after manual NoO interaction. Wire rows have 64-bit quantity at +12 and price at +68. |
| Does current ProcessEvent capture preserve full precision? | No: native conversion takes only low 32 bits of quantity, price and base price. New diagnostic `shop_wire.py` preserves 64-bit values. Production unchanged. |
| Alternate no-target shop request? | None confirmed. Empty-array opcode83 produced no fresh shop list near/far, including repeat runs through confirmed lower send after a working positive control. |
| Remote local cache? | No candidate in bounded reflected remote-actor inspection. This does not exclude native/unreflected caches. Previously opened data is not a fresh remote solution. |

See [observations](observations/001-wire-and-controls.md),
[failures and hypotheses](FAILURES.md), [commands](COMMANDS.md), and
[RESUME](RESUME.md). Tools: `tools/RemotePrices`; private runtime/captures:
`%LOCALAPPDATA%/PriceCheckCollector/research/remote-prices/19660`.

Three temporary research entry-point patches were restored and read back. The
previous radar detour remains intact. Capture allocations remain until client
exit to avoid freeing code beneath an in-flight thread. Client 19660 stayed
responsive with target zero. No production route/profile/driver/release change.

Validation: two captured A1 replies matched the three-row manual control; two
offline precision/framing tests passed; `build.ps1 -SkipBroker -OutputDirectory
workspace/remote-prices-build` passed. This build was staged, not deployed.

## Follow-up controls

See [send-chain controls](observations/002-send-controls.md). Native selection
reported send success but did not reach the observed lower callback. A guarded,
research-only adapter for the nested agent/prior relay passed NoO's stationary
ordinary-open control. ABAPKA's nine exact prices required targeting and a
379.52-unit approach; they do not meet the success criterion. The prior remote
item/quantity/enchant multiset matched all nine lots. The109.37-unit final
actor-center distance means95 is a conservative collector limit, not a proved
exact server boundary.

All seven temporary entry sites were restored/read back. The current user
deferred coordinate-spoofing research; no such test was performed.
Working collection code, profiles and deployment remain unchanged.

## Production sender fix

At the user's subsequent request, the guarded relay resolver was promoted into
the working sender and deployed to both running extracted installations. The
packaged normal price workflow passed on MHE with three fresh rows. See
[delivery and live validation](observations/003-production-send-fix.md).
This restores ordinary target/shop actions; remote no-target prices remain
unsolved. Coordinate-spoofing research remains deferred.
