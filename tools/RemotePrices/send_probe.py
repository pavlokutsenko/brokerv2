"""Observe the native send return without changing arguments or results.

The single reviewed site has no relative instructions in its displaced bytes.
This is research instrumentation, not a replacement sender.
"""
from __future__ import annotations

import argparse
import json
import struct

from lab import (Code, Lu4MemoryClient, absolute_jump, directory, guard, load,
                 patch_region, read_exact, save, write_exact)
from packet_capture import PUSH, POP

RVA = 0x4C22A0B
ORIGINAL = bytes.fromhex("8B 44 24 48 03 E8 44 8B 44 24 40 44 2B C0")
CAPACITY = 256
STRIDE = 128
STATE = "send-probe-state.json"


def stub(control: int, records: int, continuation: int) -> bytes:
    c = Code()
    c.emit(PUSH)
    c.emit(b"\x49\xBB" + struct.pack("<Q", control))
    c.emit(bytes.fromhex("BA 01 00 00 00 41 87 13 85 D2"))
    c.branch32(bytes.fromhex("0F 85"), "busy")
    c.emit(bytes.fromhex("49 8B 43 08 48 FF C0 49 89 C1 41 81 E1 FF 00 00 00 49 C1 E1 07"))
    c.emit(b"\x49\xBA" + struct.pack("<Q", records))
    c.emit(bytes.fromhex("4D 01 CA 49 C7 02 00 00 00 00"))
    # Original RAX is at saved-stack +64. Original RSP is saved-stack +80.
    c.emit(bytes.fromhex("65 48 8B 14 25 48 00 00 00 49 89 52 08"))
    c.emit(bytes.fromhex("48 8B 54 24 40 49 89 52 10"))
    c.emit(bytes.fromhex("8B 94 24 90 00 00 00 41 89 52 18"))
    c.emit(bytes.fromhex("8B 94 24 98 00 00 00 41 89 52 1C"))
    c.emit(bytes.fromhex("49 89 7A 20 48 8D 54 24 50 49 89 52 28"))
    c.emit(bytes.fromhex("48 8B 57 10 49 89 52 30 48 8B 12 48 8B 52 58 49 89 52 38"))
    c.emit(bytes.fromhex("48 8B 13 49 89 52 40 48 8B 53 08 49 89 52 48"))
    c.emit(bytes.fromhex("49 89 02 49 89 43 08 41 C7 03 00 00 00 00"))
    c.branch32(b"\xE9", "restore")
    c.label("busy")
    c.emit(bytes.fromhex("F0 49 FF 43 10"))
    c.label("restore")
    c.emit(POP)
    c.emit(ORIGINAL)
    c.emit(absolute_jump(continuation, 14))
    return c.finish()


def install(pid: int) -> None:
    if (directory(pid) / STATE).exists():
        raise RuntimeError("probe state already exists")
    with Lu4MemoryClient() as client:
        base = guard(client, pid)
        address = base + RVA
        if read_exact(client, pid, address, len(ORIGINAL)) != ORIGINAL:
            raise RuntimeError("send-return site differs; refusing to patch")
        cave, _ = client.allocate_process_memory(pid, 0x10000)
        state = {"pid": pid, "image_base": base, "address": address,
                 "cave": cave, "control": cave + 0x800, "records": cave + 0x1000,
                 "original": ORIGINAL.hex(), "patch": absolute_jump(cave, len(ORIGINAL)).hex()}
        save(pid, STATE, state)
        write_exact(client, pid, cave, stub(state["control"], state["records"], address + len(ORIGINAL)))
        write_exact(client, pid, state["control"], bytes(32))
        patch_region(client, pid, address, bytes.fromhex(state["patch"]))
    print("Send-return observer installed.")


def snapshot(pid: int, name: str) -> None:
    state = load(pid, STATE)
    with Lu4MemoryClient() as client:
        if guard(client, pid) != state["image_base"]:
            raise RuntimeError("stale process")
        if read_exact(client, pid, state["address"], len(ORIGINAL)) != bytes.fromhex(state["patch"]):
            raise RuntimeError("probe no longer installed")
        busy, sequence, dropped = struct.unpack("<I4xQQ", read_exact(client, pid, state["control"], 24))
        if busy:
            raise RuntimeError("probe busy; retry snapshot")
        rows = []
        for seq in range(max(1, sequence - CAPACITY + 1), sequence + 1):
            address = state["records"] + (seq % CAPACITY) * STRIDE
            raw = read_exact(client, pid, address, 80)
            observed, tid, return_value, requested, sent, connection, rsp, socket_object, method = struct.unpack_from("<QQQiiQQQQ", raw)
            if observed != seq or read_exact(client, pid, address, 8) != raw[:8]:
                raise RuntimeError("record changed during read")
            rows.append(dict(sequence=seq, tid=tid, return_value=return_value, requested=requested,
                             sent=sent, connection=hex(connection), rsp=hex(rsp),
                             socket_object=hex(socket_object), method=hex(method), prefix=raw[64:80].hex()))
    save(pid, name + ".json", {"sequence": sequence, "dropped": dropped, "rows": rows})
    print(json.dumps({"sequence": sequence, "dropped": dropped, "recent": rows[-12:]}, indent=2))


def restore(pid: int) -> None:
    state = load(pid, STATE)
    with Lu4MemoryClient() as client:
        if guard(client, pid) != state["image_base"]:
            raise RuntimeError("stale process")
        if read_exact(client, pid, state["address"], len(ORIGINAL)) != bytes.fromhex(state["patch"]):
            raise RuntimeError("unexpected live site")
        patch_region(client, pid, state["address"], ORIGINAL)
        if read_exact(client, pid, state["address"], len(ORIGINAL)) != ORIGINAL:
            raise RuntimeError("rollback verification failed")
    save(pid, "send-probe-restored.json", {**state, "allocations_retained_until_process_exit": True})
    (directory(pid) / STATE).unlink()
    print("Send-return observer restored and verified.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pid", type=int)
    parser.add_argument("command", choices=("install", "snapshot", "restore"))
    parser.add_argument("--name", default="send-results")
    args = parser.parse_args()
    if args.command == "snapshot":
        snapshot(args.pid, args.name)
    else:
        globals()[args.command](args.pid)
