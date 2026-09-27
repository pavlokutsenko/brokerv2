"""Reconstruct one trader's enchant multiset using only native broker queries."""
from __future__ import annotations

import argparse
from collections import Counter
import json

from broker_sweep import Observation
from lab import load, save


def run(pid: int, object_id: int, inventory: str, name: str) -> None:
    inventory_rows = load(pid, inventory)
    pairs = sorted({(r["store_type"], r["item_id"]) for r in inventory_rows if r["object_id"] == object_id})
    if not pairs:
        raise RuntimeError("trader has no rows in the source inventory")
    observation = Observation(pid, name)
    result = {"pid": pid, "object_id": object_id, "rows": [], "pairs": [], "prices_available": False}
    try:
        for store, item in pairs:
            def query(threshold):
                response = observation.query(store, item, threshold)
                return Counter((r["amount"], r["currency_id"]) for r in response["rows"] if r["object_id"] == object_id)
            first = query(0)
            current = first
            pair_rows = []
            for threshold in range(1, 256):
                following = query(threshold)
                if following - current:
                    raise RuntimeError("row multiset grew; listing changed during the enchant sweep")
                for (amount, currency), multiplicity in (current - following).items():
                    pair_rows.extend({"store_type": store, "item_id": item, "quantity": amount,
                                      "currency_id": currency, "enchant": threshold - 1,
                                      "price": None} for _ in range(multiplicity))
                if not following:
                    break
                current = following
            else:
                raise RuntimeError("nonempty threshold 255; exact upper enchant unknown")
            if query(0) != first:
                raise RuntimeError("baseline changed after enchant sweep")
            result["rows"].extend(pair_rows)
            result["pairs"].append({"store_type": store, "item_id": item, "max_threshold_queried": threshold,
                                    "rows": len(pair_rows), "baseline_rechecked": True})
    finally:
        observation.close()
        save(pid, name + "-rows.json", result)
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pid", type=int)
    parser.add_argument("object_id", type=int)
    parser.add_argument("--inventory", default="broker-sweep-1-rows.json")
    parser.add_argument("--name", default="remote-trader-enchant")
    args = parser.parse_args()
    run(args.pid, args.object_id, args.inventory, args.name)
