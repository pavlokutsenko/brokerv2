"""Observe only validated native ItemBroker calls, with target/position controls."""
from __future__ import annotations

import argparse
import contextlib
import io
import json
import struct
import time

from lab import Lu4MemoryClient, directory, guard, load, patch_region, position, read_exact, save
from packet_capture import snapshot
import process_event_shop_capture as shop
from inspect_shop_ufunctions import decode_name
from scan_lu4_actors import Memory
from invoke_process_event import invoke


def configure(pid: int) -> None:
    shop.STATE_PATH = directory(pid) / "shop-capture-state.json"
    shop.ROUTE_PATH = directory(pid) / "route.json"
    shop.FUNCTIONS_PATH = directory(pid) / "functions.json"


def install_bridge(pid: int) -> None:
    configure(pid)
    with contextlib.redirect_stdout(io.StringIO()):
        shop.install(pid)
    print("Game-thread bridge ready; shop and target UI suppression are off.")


def restore_bridge(pid: int) -> None:
    configure(pid)
    state = shop.load_state()
    with Lu4MemoryClient() as client:
        if state["pid"] != pid or guard(client, pid) != state["image_base"]:
            raise RuntimeError("stale ProcessEvent state")
        address = state["process_event"]
        original, patch = bytes.fromhex(state["original_hex"]), bytes.fromhex(state["patch_hex"])
        if read_exact(client, pid, address, len(patch)) != patch:
            raise RuntimeError("ProcessEvent hook changed; refusing rollback")
        patch_region(client, pid, address, original)
        if read_exact(client, pid, address, len(original)) != original:
            raise RuntimeError("ProcessEvent restoration failed")
    save(pid, "shop-capture-restored.json", {**state, "allocations_retained_until_process_exit": True})
    shop.STATE_PATH.unlink()
    print("ProcessEvent restored and verified; allocations retained until client exit.")


def call_broker(pid: int, store: int, item: int | None, enchant: int) -> dict:
    name = "RequestMarketItemsList" if item is None else "RequestSearchTraders"
    params = struct.pack("<i", store) if item is None else struct.pack("<iii", item, store, enchant)
    call_hud(pid, name, params)
    return {"function": name, "store_type": store, "item_id": item, "enchant_min": enchant}


def call_hud(pid: int, name: str, params: bytes) -> None:
    configure(pid)
    allowed = {"RequestMarketItemsList", "RequestSearchTraders", "ClientSelectTarget", "ClientTargetCancel", "ClientPlayerShopBuy", "ClientPlayerShopSell"}
    if name not in allowed:
        raise ValueError("not a reviewed research call")
    if name in ("ClientPlayerShopBuy", "ClientPlayerShopSell") and (len(params) != 24 or any(params[4:])):
        raise ValueError("only a completely empty item array is permitted; no transactions")
    matches = [r for r in load(pid, "all-functions.json")["matches"] if r["name"] == name and r["class_name"] == "Function"]
    if len(matches) != 1:
        raise RuntimeError(f"function not unique: {name}")
    with Lu4MemoryClient() as client:
        base = guard(client, pid)
        state = position(client, pid)
        hud = struct.unpack("<Q", read_exact(client, pid, state["controller"] + 0x340, 8))[0]
        mem = Memory(client, pid)
        pool = base + load(pid, "globals.json")["fname_pool_candidates"][0]["rva"]
        if decode_name(mem, pool, mem.i32(hud + 0x18, -1)) != "GameHUD_C":
            raise RuntimeError("HUD pointer validation failed")
        fn = matches[0]
        if len(params) != fn["params_size"]:
            raise RuntimeError("parameter size mismatch")
        if mem.i32(fn["object"] + 0x18, -1) != fn["name_id"]:
            raise RuntimeError("UFunction identity changed")
    invoke(hud, fn["object"], params)


def control(pid: int, store: int, item: int | None, enchant: int, name: str) -> None:
    capture = load(pid, "packet-capture-state.json")
    result = {"pid": pid, "packets": [], "samples": [], "dropped": {}, "actions": []}
    with Lu4MemoryClient() as client:
        if guard(client, pid) != capture["image_base"]:
            raise RuntimeError("stale capture")
        after = {h["kind"]: struct.unpack("<Q", read_exact(client, pid, h["control"] + 8, 8))[0] for h in capture["hooks"]}
        result["start_sequences"] = dict(after)
        started = time.monotonic()
        sent = False
        sample_at = 0.0
        while time.monotonic() - started < 4:
            elapsed = time.monotonic() - started
            if elapsed >= 1 and not sent:
                result["actions"].append({"elapsed": elapsed, **call_broker(pid, store, item, enchant)})
                sent = True
            for hook in capture["hooks"]:
                rows, after[hook["kind"]], dropped = snapshot(client, pid, hook, after[hook["kind"]])
                result["dropped"][hook["kind"]] = dropped
                result["packets"].extend(rows)
            if elapsed >= sample_at:
                result["samples"].append({"elapsed": elapsed, **position(client, pid)})
                sample_at += 0.05
            time.sleep(0.005)
        result["end_sequences"] = after
        result["shop_capture"] = shop.read_capture(client, shop.load_state())
    save(pid, name + ".json", result)
    print(json.dumps({"file": str(directory(pid) / (name + ".json")), "packets": len(result["packets"]),
                      "dropped": result["dropped"], "actions": result["actions"],
                      "tx": [r["hex"] for r in result["packets"] if r["direction"] == "tx"],
                      "rx": [{"length": r["length"], "head": r["hex"][:64]} for r in result["packets"] if r["direction"] == "rx"]}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pid", type=int)
    parser.add_argument("command", choices=("install-bridge", "restore-bridge", "query"))
    parser.add_argument("--store", type=int, choices=(1, 3, 8), default=1)
    parser.add_argument("--item", type=int)
    parser.add_argument("--enchant", type=int, choices=range(256), default=0)
    parser.add_argument("--name", default="broker-control")
    args = parser.parse_args()
    if args.command == "install-bridge":
        install_bridge(args.pid)
    elif args.command == "restore-bridge":
        restore_bridge(args.pid)
    else:
        control(args.pid, args.store, args.item, args.enchant, args.name)
