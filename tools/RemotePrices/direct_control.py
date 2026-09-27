"""PID-scoped control adapter for the reviewed nested send-relay layout.

Uses the existing direct sender unchanged after validating the agent MinHook
trampoline and the retained five-byte send prologue. Production is not patched.
"""
from __future__ import annotations

import argparse
import contextlib
import io
import json
import math
import struct

from lab import (Lu4MemoryClient, absolute_jump, directory, guard, load,
                 patch_region, position, read_exact, save, write_exact)
from broker_control import configure as configure_shop
from broker_sweep import Observation
import lu4_target_controller as target
import process_event_shop_capture as shop

STATE = "direct-control-state.json"


def configure(pid: int) -> None:
    target.STATE_PATH = directory(pid) / STATE
    target.DIAGNOSTICS = directory(pid)
    configure_shop(pid)


def install(pid: int) -> None:
    configure(pid)
    if target.STATE_PATH.exists():
        raise RuntimeError("direct control already installed")
    with Lu4MemoryClient() as client:
        base = guard(client, pid)
        modules = target.process_modules(client, pid)
        send = target.remote_export(modules, "ws2_32.dll", "send")
        agent = modules["pricecheck.clientagent.dll"]
        entry = read_exact(client, pid, send, 5)
        if entry[0] != 0xE9:
            raise RuntimeError("expected agent entry relay")
        relay = send + 5 + struct.unpack_from("<i", entry, 1)[0]
        raw = read_exact(client, pid, relay, 14)
        if raw[:6] != bytes.fromhex("FF 25 00 00 00 00") or struct.unpack_from("<Q", raw, 6)[0] != agent + 0x28C70:
            raise RuntimeError("unknown agent entry")
        getter = read_exact(client, pid, agent + 0x28D6C, 7)
        if getter != bytes.fromhex("48 8B 05 A5 50 0D 00"):
            raise RuntimeError("unreviewed agent real_send reference")
        trampoline = struct.unpack("<Q", read_exact(client, pid, agent + 0xFDE18, 8))[0]
        raw = read_exact(client, pid, trampoline, 14)
        if raw[:6] != bytes.fromhex("FF 25 00 00 00 00"):
            raise RuntimeError("unknown original-send trampoline")
        prior = struct.unpack_from("<Q", raw, 6)[0]
        raw = read_exact(client, pid, prior, 65)
        if (raw[44] != 0x5A or raw[50:57] != bytes.fromhex("48 FF 25 00 00 00 00")
                or struct.unpack_from("<Q", raw, 57)[0] != send + 5
                or raw[45:50] != bytes.fromhex("48 89 5C 24 08")):
            raise RuntimeError("retained send prologue failed original relay guard")
        direct, post = base + target.DIRECT_RVA, base + target.POST_RVA
        if read_exact(client, pid, direct, len(target.DIRECT_SIGNATURE)) != target.DIRECT_SIGNATURE:
            raise RuntimeError("direct site changed")
        if read_exact(client, pid, post, len(target.POST_SIGNATURE)) != target.POST_SIGNATURE:
            raise RuntimeError("post site changed")
        cave, size = client.allocate_process_memory(pid, target.CAVE_SIZE)
        main, offset = target.run_main_generator(target.HERZ_IMAGE, cave, direct, send, raw[45:50])
        post_stub = target.run_post_generator(target.HERZ_IMAGE, cave, post)
        page = bytearray(size)
        struct.pack_into("<Q", page, 0x10, cave + 0x80 + offset)
        struct.pack_into("<Q", page, 0x20, cave + 0x300)
        struct.pack_into("<Q", page, 0x60, send)
        page[0x80:0x80+len(main)] = main
        page[0xD00:0xD28] = target.critical_section_initializer()
        struct.pack_into("<Q", page, 0xD30, target.remote_export(modules, "ntdll.dll", "RtlEnterCriticalSection"))
        struct.pack_into("<Q", page, 0xD38, target.remote_export(modules, "ntdll.dll", "RtlLeaveCriticalSection"))
        page[0xE00:0xE00+len(post_stub)] = post_stub
        state = {"pid": pid, "image_base": base, "cave": cave, "cave_size": size,
                 "direct": direct, "post": post, "send": send, "prior_relay": prior,
                 "direct_original_hex": target.DIRECT_SIGNATURE.hex(), "post_original_hex": target.POST_SIGNATURE.hex(),
                 "direct_patch_hex": absolute_jump(cave+0x80, len(target.DIRECT_SIGNATURE)).hex(),
                 "post_patch_hex": absolute_jump(cave+0xE00, len(target.POST_SIGNATURE)).hex()}
        save(pid, STATE, state)
        write_exact(client, pid, cave, bytes(page))
        patch_region(client, pid, post, bytes.fromhex(state["post_patch_hex"]))
        patch_region(client, pid, direct, bytes.fromhex(state["direct_patch_hex"]))
        write_exact(client, pid, cave + 0x74, struct.pack("<I", 1))
    print("Isolated direct-control adapter installed with the original relay guard.")


def restore(pid: int) -> None:
    state = load(pid, STATE)
    with Lu4MemoryClient() as client:
        if guard(client, pid) != state["image_base"]:
            raise RuntimeError("stale state")
        write_exact(client, pid, state["cave"] + 0x74, bytes(4))
        for key in ("direct", "post"):
            original, patch = bytes.fromhex(state[key + "_original_hex"]), bytes.fromhex(state[key + "_patch_hex"])
            if read_exact(client, pid, state[key], len(original)) not in (original, patch):
                raise RuntimeError("live site changed")
            patch_region(client, pid, state[key], original)
            if read_exact(client, pid, state[key], len(original)) != original:
                raise RuntimeError("restore failed")
    save(pid, "direct-control-restored.json", {**state, "allocations_retained_until_process_exit": True})
    (directory(pid) / STATE).unlink()
    print("Direct-control sites restored and verified.")


def send(pid: int, object_id: int, xyz: dict, payload: bytes | None = None) -> dict:
    configure(pid)
    if payload is not None and payload not in (b"\x48", b"\x83" + struct.pack("<ii", object_id, 0)):
        raise ValueError("only target cancel or an empty transaction array is reviewed")
    output = io.StringIO()
    with contextlib.redirect_stdout(output):
        target.select_target(object_id, *(xyz[k] for k in "xyz"), False, fast=True, payload_override=payload)
    result = json.loads(output.getvalue())
    result["payload"] = (payload or target.build_target_packet(object_id, *(xyz[k] for k in "xyz"), False)).hex()
    if result["send_result"] != result["wire_length"]:
        raise RuntimeError("incomplete send")
    return result


def run(pid: int, object_id: int, mode: str, name: str) -> None:
    configure(pid)
    actor = next(r for r in load(pid, "actors.json")["traders"] if r["object_id"] == object_id)
    if mode == "empty-array" and actor["kiosk_type"] != 1:
        raise ValueError("empty-array probe has only been reviewed for normal sell shops")
    obs = Observation(pid, name)
    r = obs.result
    try:
        initial = position(obs.client, pid)
        if initial["selected_id"]:
            raise RuntimeError("initial target must be empty")
        address = int(actor["actor"], 16) if isinstance(actor["actor"], str) else actor["actor"]
        if struct.unpack("<i", read_exact(obs.client, pid, address+0x550, 4))[0] != object_id:
            raise RuntimeError("recycled actor")
        capsule = struct.unpack("<Q", read_exact(obs.client, pid, address+0x1A0, 8))[0]
        xyz = struct.unpack("<ddd", read_exact(obs.client, pid, capsule+0x1F0, 24))
        distance = math.dist(tuple(initial["position"].values()), xyz)
        if distance > 1500:
            raise RuntimeError("outside bounded control distance")
        r["trader"] = {"name": actor["name"], "object_id": object_id, "distance": distance}
        r["shop_before"] = shop.read_capture(obs.client, shop.load_state())
        r["samples"].append(initial)
        if mode == "open":
            for _ in range(2):
                current = position(obs.client, pid)
                r["requests"].append(send(pid, object_id, current["position"]))
                obs.wait(0.4)
                r["samples"].append(position(obs.client, pid))
        else:
            r["requests"].append(send(pid, object_id, initial["position"], b"\x83" + struct.pack("<ii", object_id, 0)))
        for _ in range(200 if mode == "open" else 30):
            obs.wait(0.1)
            r["samples"].append(position(obs.client, pid))
            current = shop.read_capture(obs.client, shop.load_state())
            if current["sequence"] > r["shop_before"]["sequence"] and current["object_id"] == object_id:
                break
        r["shop_after"] = shop.read_capture(obs.client, shop.load_state())
    finally:
        current = position(obs.client, pid)
        if mode == "open" and current["selected_id"] == object_id:
            r["requests"].append(send(pid, 0, current["position"], b"\x48"))
            obs.wait(0.5)
        obs.close()
    print(json.dumps({"trader": r["trader"], "mode": mode, "requests": r["requests"],
                      "sequences": [r["shop_before"]["sequence"], r["shop_after"]["sequence"]],
                      "rows": r["shop_after"]["rows"] if r["shop_after"]["sequence"] > r["shop_before"]["sequence"] else [],
                      "targets": sorted({s["selected_id"] for s in r["samples"]}),
                      "position_variants": len({tuple(s["position"].values()) for s in r["samples"]}),
                      "rx_short": [p["hex"] for p in r["packets"] if p["direction"] == "rx" and p["length"] < 15]}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pid", type=int)
    parser.add_argument("command", choices=("install", "restore", "open", "empty-array"))
    parser.add_argument("--object-id", type=int)
    parser.add_argument("--name", default="direct-control")
    args = parser.parse_args()
    if args.command in ("install", "restore"):
        globals()[args.command](args.pid)
    else:
        run(args.pid, args.object_id, args.command, args.name)
