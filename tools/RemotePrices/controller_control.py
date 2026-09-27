"""Ordinary target/shop control through the player's SelectTarget Blueprint.

This deliberately permits selection and approach; it cannot satisfy the remote
no-target criterion. ForceAttack stays false. No transaction is encoded.
"""
from __future__ import annotations

import argparse
import json
import math
import struct

from lab import load, position, read_exact
from broker_control import call_hud, configure
from broker_sweep import Observation
from invoke_process_event import invoke
import process_event_shop_capture as shop


def run(pid: int, object_id: int, name: str, approach: bool = False) -> None:
    configure(pid)
    actor = next(r for r in load(pid, "actors.json")["traders"] if r["object_id"] == object_id)
    observation = Observation(pid, name)
    result = observation.result
    try:
        initial = position(observation.client, pid)
        if initial["selected_id"]:
            raise RuntimeError("control requires empty initial target")
        address = int(actor["actor"], 16) if isinstance(actor["actor"], str) else actor["actor"]
        if struct.unpack("<i", read_exact(observation.client, pid, address + 0x550, 4))[0] != object_id:
            raise RuntimeError("recycled actor")
        capsule = struct.unpack("<Q", read_exact(observation.client, pid, address + 0x1A0, 8))[0]
        xyz = struct.unpack("<ddd", read_exact(observation.client, pid, capsule + 0x1F0, 24))
        distance = math.dist(tuple(initial["position"].values()), xyz)
        if distance > (1500 if approach else 95):
            raise RuntimeError("outside bounded control distance")
        route = load(pid, "route.json")
        fn = route["select_target_candidates"]
        if len(fn) != 1 or int(route["controller"], 16) != initial["controller"]:
            raise RuntimeError("stale or ambiguous route")
        fn = fn[0]
        function = int(fn["address"], 16)
        if (struct.unpack("<i", read_exact(observation.client, pid, function + 0x18, 4))[0] != 7178516
                or struct.unpack("<H", read_exact(observation.client, pid, function + 0xB6, 2))[0] != 10):
            raise RuntimeError("SelectTarget identity mismatch")
        result["trader"] = {"name": actor["name"], "object_id": object_id, "distance": distance}
        result["samples"].append(initial)
        result["shop_before"] = shop.read_capture(observation.client, shop.load_state())
        observation.wait(0.3)
        for _ in range(2):
            output = invoke(initial["controller"], function, struct.pack("<QBB", address, 0, 0))
            result["requests"].append({"function": "SelectTarget", "ForceAttack": False, "output": output.hex()})
            observation.wait(0.5)
            result["samples"].append(position(observation.client, pid))
        for _ in range(200 if approach else 30):
            observation.wait(0.1)
            result["samples"].append(position(observation.client, pid))
            current = shop.read_capture(observation.client, shop.load_state())
            if current["sequence"] > result["shop_before"]["sequence"] and current["object_id"] == object_id:
                break
        result["shop_after"] = shop.read_capture(observation.client, shop.load_state())
    finally:
        if position(observation.client, pid)["selected_id"] == object_id:
            call_hud(pid, "ClientTargetCancel", b"\0")
            observation.wait(0.5)
        observation.close()
    print(json.dumps({"trader": result["trader"], "requests": result["requests"],
                      "shop_before_sequence": result["shop_before"]["sequence"], "shop_after": result["shop_after"],
                      "targets": sorted({s["selected_id"] for s in result["samples"]}),
                      "position_variants": len({tuple(s["position"].values()) for s in result["samples"]}),
                      "tx": [r["hex"] for r in result["packets"] if r["direction"] == "tx"],
                      "rx_short": [r["hex"] for r in result["packets"] if r["direction"] == "rx" and r["length"] < 15]}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pid", type=int)
    parser.add_argument("object_id", type=int)
    parser.add_argument("--name", required=True)
    parser.add_argument("--approach", action="store_true")
    args = parser.parse_args()
    run(args.pid, args.object_id, args.name, args.approach)
