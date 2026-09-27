"""Sequential native broker observations with exact packet-length validation."""
from __future__ import annotations

import argparse
import json
import struct
import time

from lab import Lu4MemoryClient, directory, guard, load, position, read_exact, save
from broker_control import call_broker
from packet_capture import snapshot


def decode(raw: bytes) -> dict | None:
    if raw[:3] == bytes.fromhex("feec01"):
        store, count = struct.unpack_from("<BH", raw, 3)
        if len(raw) != 6 + 4 * count:
            raise ValueError("market response length differs from known grammar")
        return {"kind": "market", "store_type": store, "items": list(struct.unpack_from(f"<{count}i", raw, 6))}
    if raw[:3] == bytes.fromhex("feed01"):
        item, store, count = struct.unpack_from("<iBH", raw, 3)
        if len(raw) != 10 + 16 * count:
            raise ValueError("broker response length differs from known grammar")
        return {"kind": "search", "item_id": item, "store_type": store,
                "rows": [dict(zip(("object_id", "amount", "currency_id"), struct.unpack_from("<iqi", raw, 10 + i * 16))) for i in range(count)]}
    return None


class Observation:
    def __init__(self, pid: int, name: str):
        self.pid, self.name = pid, name
        self.state = load(pid, "packet-capture-state.json")
        self.client = Lu4MemoryClient()
        self.client.__enter__()
        if guard(self.client, pid) != self.state["image_base"]:
            raise RuntimeError("stale packet capture")
        self.after = {h["kind"]: struct.unpack("<Q", read_exact(self.client, pid, h["control"] + 8, 8))[0] for h in self.state["hooks"]}
        self.result = {"pid": pid, "name": name, "samples": [], "packets": [], "requests": [], "dropped": {}}
        self.stream = (directory(pid) / (name + ".jsonl")).open("w", encoding="utf-8")

    def pump(self) -> list[dict]:
        decoded = []
        for h in self.state["hooks"]:
            rows, self.after[h["kind"]], drops = snapshot(self.client, self.pid, h, self.after[h["kind"]])
            self.result["dropped"][h["kind"]] = drops
            for row in rows:
                self.stream.write(json.dumps(row) + "\n")
                if row["copied"] != row["length"]:
                    raise RuntimeError("truncated packet")
                if row["direction"] == "rx":
                    value = decode(bytes.fromhex(row["hex"]))
                    if value is not None:
                        decoded.append(value)
            self.result["packets"].extend(rows)
        self.stream.flush()
        return decoded

    def wait(self, seconds: float) -> list[dict]:
        deadline = time.monotonic() + seconds
        result = []
        while time.monotonic() < deadline:
            result.extend(self.pump())
            time.sleep(0.005)
        return result

    def query(self, store: int, item: int | None = None, enchant: int = 0) -> dict:
        self.pump()
        before = position(self.client, self.pid)
        self.result["samples"].append(before)
        if before["selected_id"]:
            raise RuntimeError("broker no-target experiment requires an empty target")
        action = call_broker(self.pid, store, item, enchant)
        self.result["requests"].append(action)
        deadline = time.monotonic() + 3
        while time.monotonic() < deadline:
            for response in self.pump():
                if response["store_type"] == store and response.get("item_id") == item:
                    action["response"] = response
                    self.result["samples"].append(position(self.client, self.pid))
                    self.wait(0.08)
                    return response
            time.sleep(0.005)
        raise TimeoutError(f"no matching broker response: {action}")

    def close(self):
        try:
            self.pump()
            self.result["samples"].append(position(self.client, self.pid))
        finally:
            save(self.pid, self.name + ".json", self.result)
            self.stream.close()
            self.client.__exit__(None, None, None)


def run(pid: int, name: str, stores: list[int]) -> None:
    observation = Observation(pid, name)
    try:
        for store in stores:
            market = observation.query(store)
            for item in market["items"]:
                observation.query(store, item)
            print(json.dumps({"store": store, "items": len(market["items"])}), flush=True)
    finally:
        observation.close()
    rows = [{"item_id": r["item_id"], "store_type": r["store_type"], **row}
            for r in observation.result["requests"] if r["item_id"] is not None
            for row in r["response"]["rows"]]
    save(pid, name + "-rows.json", rows)
    print(json.dumps({"rows": len(rows), "traders": len({r["object_id"] for r in rows}), "output": str(directory(pid) / (name + ".json"))}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pid", type=int)
    parser.add_argument("--name", default="broker-sweep")
    parser.add_argument("--stores", default="1,3,8")
    args = parser.parse_args()
    run(args.pid, args.name, [int(x) for x in args.stores.split(",")])
