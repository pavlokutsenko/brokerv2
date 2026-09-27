# Broker enchant discovery

The official ItemBroker search packet contains an `EnchantMin` byte:

```text
20 <item_id:i32le> <store_type:u8> <enchant_min:u8>
```

The response row still contains only trader object ID, the nested currency/item
ID, and amount. It does not carry an explicit enchant field. Exact enchant can
nevertheless be reconstructed from the difference between successive minimum
enchant queries. Responses preserve duplicate listing rows from one trader, so
the comparison must use a multiset rather than a set.

## Live proof (2026-09-20)

`probe_broker_enchant.py` queried sell item `34932` on live PID `10532`:

| EnchantMin | Rows |
|---:|---:|
| 0 | 14 |
| 1 | 8 |
| 2 | 8 |
| 3 | 8 |
| 4 | 8 |
| 5 | 0 |

The result proves six `+0` rows and eight `+4` rows. One trader occurred five
times in the `+0` result, confirming that duplicate rows are retained rather
than collapsed to one row per trader.

Artifact: `docs/analysis/broker-enchant/probe-34932.json`.

## Integration consequence

The current stage-one database key `(traderId, itemId, side)` cannot represent
the same item at multiple enchant levels. Before broker enchant is uploaded,
`enchantLevel` must be added to the ingest contract and to the `TraderItem`
identity. The collector must group by `(trader, item, side, enchantLevel)`.

For a complete broker pass, collect the existing threshold-zero snapshot, then
query threshold one for all `(store type, item)` pairs. Continue higher
thresholds only for pairs that still return rows. For each pair, subtract the
multiset returned at threshold `n + 1` from threshold `n`; the remainder has
exact enchant `n`. Abort or retry a pair when its row multiset grows between
thresholds, since that means the listing changed while the sweep was running.
