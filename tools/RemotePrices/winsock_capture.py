"""Observe the existing agent's lower-send callback; preserve its entire chain."""
from __future__ import annotations

import argparse
import json
import struct

from lab import (Lu4MemoryClient, absolute_jump, directory, guard, load,
                 patch_region, read_exact, save, write_exact)
from packet_capture import CAPACITY, SLOT_SIZE, build_stub, snapshot
from lu4_target_controller import process_modules, remote_export

STATE = "winsock-capture-state.json"
ORIGINAL = bytes.fromhex("40 55 53 56 57 41 54 41 55 41 56 41 57 48 8D 6C 24 E8")


def install(pid: int) -> None:
    if (directory(pid) / STATE).exists():
        raise RuntimeError("observer already installed")
    with Lu4MemoryClient() as client:
        base = guard(client, pid)
        modules = process_modules(client, pid)
        send = remote_export(modules, "ws2_32.dll", "send")
        raw = read_exact(client, pid, send + 5, 5)
        if raw[0] != 0xE9:
            raise RuntimeError("expected lower-send relay")
        relay = send + 10 + struct.unpack_from("<i", raw, 1)[0]
        raw = read_exact(client, pid, relay, 14)
        if raw[:6] != bytes.fromhex("FF 25 00 00 00 00"):
            raise RuntimeError("unexpected relay")
        address = struct.unpack_from("<Q", raw, 6)[0]
        if address != modules["pricecheck.clientagent.dll"] + 0x27E30:
            raise RuntimeError("unreviewed agent callback")
        if read_exact(client, pid, address, len(ORIGINAL)) != ORIGINAL:
            raise RuntimeError("callback prologue mismatch")
        cave, _ = client.allocate_process_memory(pid, 0x1000)
        records = []
        try:
            for _ in range(CAPACITY):
                record, _ = client.allocate_process_memory(pid, SLOT_SIZE)
                records.append(record)
        except Exception:
            for record in records:
                client.free_process_memory(pid, record)
            client.free_process_memory(pid, cave)
            raise
        state = {"pid": pid, "image_base": base, "kind": "winsock", "address": address,
                 "cave": cave, "control": cave + 0x800, "records": records,
                 "patch": absolute_jump(cave, len(ORIGINAL)).hex(), "original": ORIGINAL.hex()}
        save(pid, STATE, state)
        write_exact(client, pid, cave, build_stub("winsock", state["control"], cave + 0x900, ORIGINAL, address + len(ORIGINAL)))
        write_exact(client, pid, state["control"], bytes(32))
        write_exact(client, pid, cave + 0x900, struct.pack(f"<{CAPACITY}Q", *records))
        patch_region(client, pid, address, bytes.fromhex(state["patch"]))
    print("Existing lower-send callback observer installed; forwarding unchanged.")


def dump(pid: int, name: str) -> None:
    state = load(pid, STATE)
    with Lu4MemoryClient() as client:
        if guard(client, pid) != state["image_base"]:
            raise RuntimeError("stale image")
        sequence = struct.unpack("<Q", read_exact(client, pid, state["control"] + 8, 8))[0]
        rows, sequence, dropped = snapshot(client, pid, state, max(0, sequence - CAPACITY))
    save(pid, name + ".json", {"rows": rows, "sequence": sequence, "dropped": dropped})
    print(json.dumps({"sequence": sequence, "dropped": dropped, "lengths": [r["length"] for r in rows]}))


def restore(pid: int) -> None:
    state = load(pid, STATE)
    with Lu4MemoryClient() as client:
        if guard(client, pid) != state["image_base"]:
            raise RuntimeError("stale image")
        if read_exact(client, pid, state["address"], len(ORIGINAL)) != bytes.fromhex(state["patch"]):
            raise RuntimeError("live callback changed")
        patch_region(client, pid, state["address"], ORIGINAL)
        if read_exact(client, pid, state["address"], len(ORIGINAL)) != ORIGINAL:
            raise RuntimeError("restoration failed")
    save(pid, "winsock-capture-restored.json", {**state, "allocations_retained_until_process_exit": True})
    (directory(pid) / STATE).unlink()
    print("Lower-send callback restored and verified.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pid", type=int)
    parser.add_argument("command", choices=("install", "dump", "restore"))
    parser.add_argument("--name", default="winsock-observation")
    args = parser.parse_args()
    if args.command == "dump":
        dump(args.pid, args.name)
    else:
        globals()[args.command](args.pid)
