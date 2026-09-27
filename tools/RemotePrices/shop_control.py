"""Bounded shop controls. A normal-open run is explicitly a target-based control.

An empty-array probe can request no item and cannot encode a purchase/sale row.
It is a hypothesis test, not a claimed price-reading implementation.
"""
from __future__ import annotations

import argparse
import json
import math
import struct
import time

from lab import load, position, read_exact
from broker_control import call_hud, configure
from broker_sweep import Observation
import process_event_shop_capture as shop


def run(pid: int, object_id: int, mode: str, name: str) -> None:
    configure(pid)
    actor = next(r for r in load(pid, "actors.json")["traders"] if r["object_id"] == object_id)
    observation = Observation(pid, name)
    result = observation.result
    try:
        initial = position(observation.client, pid)
        if initial["selected_id"] and mode != "selected-control":
            raise RuntimeError("requires an initially empty target")
        if mode == "selected-control" and initial["selected_id"] != object_id:
            raise RuntimeError("selected control requires this trader already selected")
        # Validate the live actor token and coordinates; do not trust snapshot distance.
        address = int(actor["actor"], 16) if isinstance(actor["actor"], str) else actor["actor"]
        if struct.unpack("<i", read_exact(observation.client, pid, address + 0x550, 4))[0] != object_id:
            raise RuntimeError("trader actor was recycled")
        capsule = struct.unpack("<Q", read_exact(observation.client, pid, address + 0x1A0, 8))[0]
        xyz = struct.unpack("<ddd", read_exact(observation.client, pid, capsule + 0x1F0, 24))
        distance = math.dist(tuple(initial["position"].values()), xyz)
        result["trader"] = {"name": actor["name"], "object_id": object_id, "distance": distance, "kiosk_type": actor["kiosk_type"]}
        result["samples"].append(initial)
        result["shop_before"] = shop.read_capture(observation.client, shop.load_state())
        observation.wait(0.5)
        if mode in ("normal-open", "selected-control"):
            if distance > 95:
                raise RuntimeError("normal-open control restricted to validated nearby range")
            params = bytearray(33)
            struct.pack_into("<i", params, 0, object_id)
            struct.pack_into("<ddd", params, 8, *initial["position"].values())
            params[32] = 0  # ordinary native action, restricted to a nearby control
            for _ in range(1 if mode == "selected-control" else 2):
                call_hud(pid, "ClientSelectTarget", bytes(params))
                result["requests"].append({"function": "ClientSelectTarget", "object_id": object_id, "bLockMovement": False})
                observation.wait(0.3)
                result["samples"].append(position(observation.client, pid))
        else:
            function = "ClientPlayerShopBuy" if actor["kiosk_type"] in (1, 8) else "ClientPlayerShopSell"
            params = struct.pack("<i", object_id) + bytes(20)
            call_hud(pid, function, params)
            result["requests"].append({"function": function, "object_id": object_id, "item_count": 0})
        for _ in range(40):
            observation.wait(0.05)
            result["samples"].append(position(observation.client, pid))
        result["shop_after"] = shop.read_capture(observation.client, shop.load_state())
    finally:
        if mode in ("normal-open", "selected-control"):
            current = position(observation.client, pid)
            if current["selected_id"] == object_id:
                call_hud(pid, "ClientTargetCancel", b"\0")
                result["requests"].append({"function": "ClientTargetCancel", "cleanup": True})
                observation.wait(0.5)
        observation.close()
    print(json.dumps({"trader": result["trader"], "mode": mode,
                      "shop_before_sequence": result["shop_before"]["sequence"],
                      "shop_after": result["shop_after"],
                      "targets": sorted({s["selected_id"] for s in result["samples"]}),
                      "position_variants": len({tuple(s["position"].values()) for s in result["samples"]}),
                      "tx": [r["hex"] for r in result["packets"] if r["direction"] == "tx"],
                      "rx_short": [r["hex"] for r in result["packets"] if r["direction"] == "rx" and r["length"] < 15]}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pid", type=int)
    parser.add_argument("object_id", type=int)
    parser.add_argument("mode", choices=("normal-open", "selected-control", "empty-array"))
    parser.add_argument("--name", required=True)
    args = parser.parse_args()
    run(args.pid, args.object_id, args.mode, args.name)
