"""Decode the observed normal sell-shop reply before the client's int32 narrowing.

Validated only for this build's opcode A1 and fixed 84-byte entries. Other shop
reply kinds must be independently decoded; do not guess a shared layout.
"""
from __future__ import annotations

import argparse
import json
import struct
from pathlib import Path


def decode_sell_shop(raw: bytes) -> dict:
    if len(raw) < 21 or raw[0] != 0xA1:
        raise ValueError("not a normal sell-shop reply")
    trader = struct.unpack_from("<i", raw, 1)[0]
    count = struct.unpack_from("<I", raw, 17)[0]
    if len(raw) != 21 + count * 84:
        raise ValueError("unrecognized or truncated A1 reply layout")
    rows = []
    for index in range(count):
        offset = 21 + index * 84
        rows.append({"row_index": index,
                     "item_object_id": struct.unpack_from("<i", raw, offset)[0],
                     "item_id": struct.unpack_from("<i", raw, offset + 4)[0],
                     "quantity": struct.unpack_from("<q", raw, offset + 12)[0],
                     "enchant": struct.unpack_from("<H", raw, offset + 30)[0],
                     "price": struct.unpack_from("<q", raw, offset + 68)[0],
                     "base_price": struct.unpack_from("<q", raw, offset + 76)[0]})
    return {"object_id": trader, "row_count": count, "rows": rows}


def decode_buy_shop(raw: bytes) -> dict:
    """Read the BE buy reply before the client narrows prices and quantities."""
    if len(raw) < 17 or raw[0] != 0xBE:
        raise ValueError("not a normal buy-shop reply")
    trader = struct.unpack_from("<i", raw, 1)[0]
    count = struct.unpack_from("<I", raw, 13)[0]
    if trader <= 0 or count > 200 or len(raw) != 17 + count * 96:
        raise ValueError("unrecognized or truncated BE reply layout")
    rows = []
    for index in range(count):
        offset = 17 + index * 96
        wire_item_code, item_id = struct.unpack_from("<ii", raw, offset)
        price = struct.unpack_from("<q", raw, offset + 72)[0]
        quantity = struct.unpack_from("<q", raw, offset + 88)[0]
        if wire_item_code <= 0 or item_id <= 0 or price < 0 or quantity < 0:
            raise ValueError("invalid BE buy row")
        rows.append({"row_index": index, "wire_item_code": wire_item_code,
                     "item_id": item_id, "quantity": quantity, "price": price})
    return {"object_id": trader, "row_count": count, "rows": rows, "side": "buy"}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    args = parser.parse_args()
    capture = json.loads(args.capture.read_text(encoding="utf-8-sig"))
    print(json.dumps([decode_sell_shop(bytes.fromhex(p["hex"])) for p in capture["packets"]
                      if p["direction"] == "rx" and p["hex"].startswith("a1")], indent=2))
