"""Compare previously captured wire data with a later shop's exact values.

No matches is limited to the observed windows and fixed-width LE encodings;
it does not exclude an unobserved request, compression or a native local cache.
"""
from __future__ import annotations

import argparse
import collections
import json
import struct

from lab import directory, load, save
from shop_wire import decode_sell_shop


def run(pid: int, reference: str, captures: list[str]) -> None:
    events = load(pid, reference)["packets"]
    lists = [decode_sell_shop(bytes.fromhex(p["hex"])) for p in events
             if p["direction"] == "rx" and p["hex"].startswith("a1")]
    if len(lists) != 1:
        raise RuntimeError("reference must contain one full A1 reply")
    rows = lists[0]["rows"]
    patterns = []
    for row in rows:
        patterns.append((row["item_id"], "item_object_id", 4, struct.pack("<i", row["item_object_id"])))
        patterns.append((row["item_id"], "price", 8, struct.pack("<q", row["price"])))
        if 0 <= row["price"] <= 0xFFFFFFFF:
            patterns.append((row["item_id"], "price", 4, struct.pack("<I", row["price"])))
    result = {"reference": reference, "shop": lists[0], "comparisons": []}
    for name in captures:
        packets = load(pid, name)["packets"]
        incoming = [p for p in packets if p["direction"] == "rx"]
        found = []
        for p in incoming:
            raw = bytes.fromhex(p["hex"])
            for item, field, width, pattern in patterns:
                offset = raw.find(pattern)
                while offset >= 0:
                    found.append({"sequence": p["sequence"], "opcode": raw[:3].hex(), "offset": offset,
                                  "item_id": item, "field": field, "width": width})
                    offset = raw.find(pattern, offset + 1)
        result["comparisons"].append({"capture": name, "rx_packets": len(incoming),
                                       "rx_bytes": sum(p["length"] for p in incoming), "matches": found})
    broker = load(pid, "remote-ABAPKA-enchant-rows.json")
    if broker["object_id"] != lists[0]["object_id"]:
        raise RuntimeError("broker reference belongs to another trader")
    result["membership_match"] = collections.Counter((r["item_id"], r["quantity"], r["enchant"]) for r in rows) == collections.Counter(
        (r["item_id"], r["quantity"], r["enchant"]) for r in broker["rows"])
    save(pid, "ABAPKA-price-evidence.json", result)
    print(json.dumps({"membership_match": result["membership_match"],
                      "comparisons": [{**c, "matches": len(c["matches"])} for c in result["comparisons"]]}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pid", type=int)
    parser.add_argument("reference")
    parser.add_argument("captures", nargs="+")
    args = parser.parse_args()
    run(args.pid, args.reference, args.captures)
