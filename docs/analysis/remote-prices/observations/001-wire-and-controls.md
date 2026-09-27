# Wire grammar and controls, 2026-09-24

Live PID 19660, image base 0x7FF616560000. No CE/debugger/Arduino/OS input
injection. User performed the manual positive control. One saved profile owned
this PID; other saved profiles had no PID. Existing Collector processes kept.

## Instrumentation

- Fresh driver read returned MZ; PE timestamp/image size matched guarded profile.
- GWorld RVA 81F6A80; current controller/pawn chain verified. Controller+340
  points to a validated GameHUD_C.
- RequestMarketItemsList index 19085, parameter size 4; RequestSearchTraders
  index 19086, size 12. Serializer RVAs 4BD3120 and 4BD3450.
- RX RVA 4C1D77C: r14=data, esi=length. Existing radar detour validated with
  its three ring pointers and chained unchanged.
- TX/encrypt RVA 4C009A0: rdx=data, r8=length pointer. Original 14-byte prefix
  preserved; it has no relative branch. Independent bounded rings, sequence,
  truncation and overrun checks; captures below have zero lock drops.
- ProcessEvent hook used existing shop capture with isolated LocalAppData
  state. UI/target suppression remained off. No cross-PID diagnostic copies.

## Broker wire format

```text
Request: 21 <store:u8>
Reply:   FE EC 01 <store:u8> <N:u16le> <item:i32le>*N
         length = 6 + 4*N

Request: 20 <item:i32le> <store:u8> <enchant_min:u8>
Reply:   FE ED 01 <item:i32le> <store:u8> <N:u16le>
         (<trader_object_id:i32le> <amount:i64le> <currency_id:i32le>)*N
         length = 10 + 16*N
```

broker-sweep-1.json: 103 sell, 36 buy, 11 package item queries plus three market
queries; 353 rows, 102 unique trader IDs. Every reply matched query identity and
exact byte length. Background outgoing 03/65/67 also occurred; their semantics
were not re-established. No outgoing 0F action, 0C movement or 83 request in
this pass. Every position sample was equal and every selected target null.

remote-ABAPKA-enchant-rows.json: trader 1291927129, initially ~501 units distant.
Item/quantity pairs: 64/1, 71/1, 951/1, 1458/5224, 6572/1, 6573/1, 6575/1,
6576/10, 9910/157. All currency 57, reconstructed enchant 0, price null.
Each pair used threshold 0, threshold 1, then threshold 0 again. Multiset
checks passed; position constant, target null, drops zero. Price-only changes
and indistinguishable listing replacements cannot be detected by these checks.

## Manual NoO control

User confirmed ordinary character mode and opened NoO (1329687743). Capture
manual-shop-reference.json contains four 0F actions and two identical 273-byte
A1 replies. Position changed slightly and target became NoO. This is ordinary
interaction and does not satisfy the remote/no-target criterion.

| Item | Broker amount | Shop count | Enchant | Price | Base price |
|---|---:|---:|---:|---:|---:|
| 13 | 1 | 1 | 0 | 989894747 | 590 |
| 8619 | 2 | 2 | 0 | 1150000 | 0 |
| 8618 | 4 | 4 | 0 | 135000 | 0 |

The complete three-row membership matches broker-sweep-1-rows.json. Reflected
capture is manual-shop-capture.json. This directly separates amount, actual
price and base price.

## Full-width price source

A1: trader ObjectID at packet+1, u32 row count at +17, rows at +21, stride 84.
Row offsets: item ObjectID +0/i32, item ID +4/i32, quantity +12/i64,
enchant +30/u16, price +68/i64, base price +76/i64.

Parser loop RVA 4C10CD0 advances wire pointer by 54h and builds 60h-byte native
rows. Qword quantity/price/base-price land at native +10h/+50h/+58h.
Conversion loop at 4CC3180 builds reflected 38h-byte rows. Instructions at
4CC31B0, 4CC31F6, 4CC3200 load **eax** from those source fields; destination
offsets are +8/+2Ch/+34h. The high halves are lost before Python capture.

shop_wire.py decodes A1 before narrowing. Both manual replies matched all
three reflected rows. Synthetic values above 2^32 preserved high bits; malformed
and truncated layouts rejected. No live high-price overflow was observed.
Other buy/package/new-shop reply layouts are not covered by this decoder.

## Local-state inspection scope

reflected-market-state.json records inspected controller, HUD and ABAPKA class
chains. The remote actor exposed StoreName/IsTradePose/IsTradeOptimize, no
listing-price array. HUD exposed item/shop/broker widget references. After NoO
opened, WndShop was a TestWindowsFlex_C wrapper; the broker widget was not
instantiated by native queries. No whole-heap/native-cache absence is claimed.
