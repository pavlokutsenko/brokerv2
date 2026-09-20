from __future__ import annotations

import argparse
import json
import struct
import sys
import time
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "client"))

from lu4_memory_client import Lu4MemoryClient  # noqa: E402
from lu4_target_controller import (  # noqa: E402
    absolute_jump,
    patch_region,
    read_exact,
    write_exact,
)
from process_event_shop_capture import Code, PROLOGUE  # noqa: E402


STATE_PATH = ROOT / "build" / "process_event_broker_capture_state.json"
ROUTE_PATH = ROOT / "diagnostics" / "latest_target_route.json"
FUNCTIONS_PATH = ROOT / "diagnostics" / "latest_shop_ufunctions.json"
CAPTURE_NAMES = ("BrokerMarketItemsList", "BrokerTradersByItem")
DATA_OFFSET = 0x1000
ROWS_OFFSET = 0x100
MAX_ROWS = 2048
BROKER_ROW_SIZE = 0x18
DATA_SIZE = ROWS_OFFSET + MAX_ROWS * BROKER_ROW_SIZE
HISTORY_DEPTH = 40
RECORD_STRIDE = DATA_SIZE
CAVE_SIZE = 0x10000


def build_stub(
    name_indices: dict[str, int],
    data_address: int,
    record_addresses: list[int],
    continuation: int,
) -> bytes:
    code = Code()
    code.emit(bytes.fromhex("9C 50 51 52 41 50 41 51 41 52 41 53 56 57"))
    code.emit(bytes.fromhex("8B 42 18"))
    for name_index in name_indices.values():
        code.emit(b"\x3D" + struct.pack("<I", name_index))
        code.branch32(bytes.fromhex("0F 84"), "capture")
    code.branch32(b"\xE9", "restore")

    code.label("capture")
    code.emit(b"\x48\xB8" + struct.pack("<Q", data_address))
    code.emit(bytes.fromhex("41 B9 01 00 00 00"))
    code.emit(bytes.fromhex("44 87 08"))
    code.emit(bytes.fromhex("45 85 C9"))
    code.branch32(bytes.fromhex("0F 85"), "restore")
    # Preserve a full burst before replacing slot zero. Copy backwards so the
    # source of every shift is still the previous complete record.
    for source_slot in range(HISTORY_DEPTH - 2, -1, -1):
        destination_slot = source_slot + 1
        code.emit(b"\x48\xBE" + struct.pack("<Q", record_addresses[source_slot]))
        code.emit(b"\x48\xBF" + struct.pack("<Q", record_addresses[destination_slot]))
        code.emit(b"\xB9" + struct.pack("<I", RECORD_STRIDE))
        code.emit(bytes.fromhex("F3 A4"))
        code.emit(b"\x48\xB8" + struct.pack("<Q", data_address))
        code.emit(b"\x48\xBF" + struct.pack("<Q", record_addresses[destination_slot]))
        code.emit(bytes.fromhex("C7 07 00 00 00 00"))
    code.emit(bytes.fromhex("48 89 48 10"))
    code.emit(bytes.fromhex("48 89 50 18"))
    code.emit(bytes.fromhex("4C 89 40 20"))
    code.emit(bytes.fromhex("4C 8B 4C 24 50"))  # ProcessEvent return address
    code.emit(bytes.fromhex("4C 89 88 98 00 00 00"))
    code.emit(bytes.fromhex("4C 8B 0C 24"))  # saved RDI
    code.emit(bytes.fromhex("4C 89 88 A0 00 00 00"))
    code.emit(bytes.fromhex("4C 8B 4C 24 08"))  # saved RSI
    code.emit(bytes.fromhex("4C 89 88 A8 00 00 00"))
    # BrokerTradersByItem's receive thunk has a 48h-byte frame, so its caller
    # return address is at [rsp+A0h] after the hook's ten pushes.
    code.emit(bytes.fromhex("4C 8B 8C 24 A0 00 00 00"))
    code.emit(bytes.fromhex("4C 89 88 B0 00 00 00"))
    # The conversion function above that thunk has a 38h-byte frame.  From
    # ProcessEvent, its own caller return address is at [rsp+E0h].
    code.emit(bytes.fromhex("4C 8B 8C 24 E0 00 00 00"))
    code.emit(bytes.fromhex("4C 89 88 B8 00 00 00"))
    code.emit(bytes.fromhex("44 8B 5A 18"))
    code.emit(bytes.fromhex("44 89 58 68"))
    code.emit(bytes.fromhex("C7 80 90 00 00 00 00 00 00 00"))
    code.emit(bytes.fromhex("4D 85 C0"))
    code.branch32(bytes.fromhex("0F 84"), "publish")
    for offset in range(0, 0x40, 8):
        if offset == 0:
            code.emit(bytes.fromhex("4D 8B 08"))
        else:
            code.emit(bytes.fromhex("4D 8B 48") + bytes([offset]))
        code.emit(bytes.fromhex("4C 89 48") + bytes([0x28 + offset]))

    code.emit(bytes.fromhex("45 8B 08"))
    code.emit(bytes.fromhex("44 89 48 70"))
    code.emit(bytes.fromhex("45 8B 48 04"))
    code.emit(bytes.fromhex("44 89 48 74"))
    market_index = name_indices["BrokerMarketItemsList"]
    code.emit(bytes.fromhex("41 81 FB") + struct.pack("<I", market_index))
    code.branch32(bytes.fromhex("0F 84"), "market")

    code.emit(bytes.fromhex("C6 40 78 02"))
    code.emit(bytes.fromhex("41 BA 18 00 00 00"))
    code.branch32(b"\xE9", "array")
    code.label("market")
    code.emit(bytes.fromhex("C6 40 78 01"))
    code.emit(bytes.fromhex("41 BA 04 00 00 00"))

    code.label("array")
    code.emit(bytes.fromhex("49 8B 70 08"))
    code.emit(bytes.fromhex("41 8B 50 10"))
    code.emit(bytes.fromhex("41 8B 48 14"))
    code.emit(bytes.fromhex("48 89 B0 80 00 00 00"))
    code.emit(bytes.fromhex("89 90 88 00 00 00"))
    code.emit(bytes.fromhex("89 88 8C 00 00 00"))
    code.emit(bytes.fromhex("44 89 90 94 00 00 00"))
    code.emit(bytes.fromhex("48 85 F6"))
    code.branch32(bytes.fromhex("0F 84"), "publish")
    code.emit(bytes.fromhex("85 D2"))
    code.branch32(bytes.fromhex("0F 8E"), "publish")
    code.emit(bytes.fromhex("81 FA 00 08 00 00"))
    code.branch32(bytes.fromhex("0F 8F"), "publish")
    code.emit(bytes.fromhex("39 D1"))
    code.branch32(bytes.fromhex("0F 8C"), "publish")
    code.emit(bytes.fromhex("81 F9 00 00 01 00"))
    code.branch32(bytes.fromhex("0F 8F"), "publish")
    code.emit(bytes.fromhex("89 90 90 00 00 00"))
    code.emit(bytes.fromhex("89 D1"))
    code.emit(bytes.fromhex("41 0F AF CA"))
    code.emit(b"\x48\xBF" + struct.pack("<Q", data_address + ROWS_OFFSET))
    code.emit(bytes.fromhex("F3 A4"))

    code.label("publish")
    code.emit(bytes.fromhex("F0 48 FF 40 08"))
    code.emit(bytes.fromhex("C7 00 00 00 00 00"))
    code.label("restore")
    code.emit(bytes.fromhex("5F 5E 41 5B 41 5A 41 59 41 58 5A 59 58 9D"))
    code.emit(PROLOGUE)
    code.emit(absolute_jump(continuation, 14))
    return code.finish()


def save_state(state: dict[str, object]) -> None:
    STATE_PATH.parent.mkdir(parents=True, exist_ok=True)
    temp = STATE_PATH.with_suffix(".tmp")
    temp.write_text(json.dumps(state, indent=2) + "\n", encoding="utf-8")
    temp.replace(STATE_PATH)


def load_state() -> dict[str, object]:
    if not STATE_PATH.exists():
        raise RuntimeError("broker capture hook is not installed")
    return json.loads(STATE_PATH.read_text(encoding="utf-8"))


def resolve_inputs(pid: int) -> tuple[int, dict[str, dict[str, int]]]:
    route = json.loads(ROUTE_PATH.read_text(encoding="utf-8"))
    functions = json.loads(FUNCTIONS_PATH.read_text(encoding="utf-8"))
    if int(route["pid"]) != pid or int(functions["pid"]) != pid:
        raise RuntimeError("route/UFunction diagnostics do not match live PID")
    matches = {
        item["name"]: {"address": int(item["object"]), "name_index": int(item["name_id"])}
        for item in functions.get("matches", [])
        if item.get("name") in CAPTURE_NAMES
    }
    missing = [name for name in CAPTURE_NAMES if name not in matches]
    if missing:
        raise RuntimeError(f"missing broker UFunctions: {missing}")
    return int(route["process_event"], 16), matches


def install(pid: int) -> None:
    process_event, functions = resolve_inputs(pid)
    with Lu4MemoryClient() as client:
        image_base = client.process_base(pid)
        original = read_exact(client, pid, process_event, len(PROLOGUE))
        if original != PROLOGUE:
            raise RuntimeError("ProcessEvent is already patched or has an unexpected prologue")
        cave, cave_size = client.allocate_process_memory(pid, CAVE_SIZE)
        history_caves: list[tuple[int, int]] = []
        try:
            for _ in range(HISTORY_DEPTH - 1):
                history_caves.append(
                    client.allocate_process_memory(pid, RECORD_STRIDE)
                )
        except Exception:
            for address, _ in history_caves:
                client.free_process_memory(pid, address)
            client.free_process_memory(pid, cave)
            raise
        data_address = cave + DATA_OFFSET
        record_addresses = [data_address] + [address for address, _ in history_caves]
        stub = build_stub(
            {name: value["name_index"] for name, value in functions.items()},
            data_address,
            record_addresses,
            process_event + len(PROLOGUE),
        )
        patch = absolute_jump(cave, len(PROLOGUE))
        patched = False
        try:
            write_exact(client, pid, cave, stub)
            write_exact(client, pid, data_address, b"\0" * DATA_SIZE)
            for address, _ in history_caves:
                write_exact(client, pid, address, b"\0" * DATA_SIZE)
            patch_region(client, pid, process_event, patch)
            patched = True
        except Exception:
            if patched:
                patch_region(client, pid, process_event, original)
            for address, _ in history_caves:
                client.free_process_memory(pid, address)
            client.free_process_memory(pid, cave)
            raise
        state = {
            "version": 3,
            "pid": pid,
            "image_base": image_base,
            "process_event": process_event,
            "original_hex": original.hex(),
            "patch_hex": patch.hex(),
            "cave": cave,
            "cave_size": cave_size,
            "data": data_address,
            "history_caves": [
                {"address": address, "size": size}
                for address, size in history_caves
            ],
            "record_addresses": record_addresses,
            "history_depth": HISTORY_DEPTH,
            "stub_size": len(stub),
            "patch_mode": "live-driver-write",
            "functions": functions,
        }
        save_state(state)
    print(json.dumps(state, indent=2))


def read_capture_at(
    client: Lu4MemoryClient,
    state: dict[str, object],
    data_address: int,
) -> dict[str, object]:
    pid = int(state["pid"])
    for _ in range(20):
        raw = read_exact(client, pid, data_address, DATA_SIZE)
        busy = struct.unpack_from("<I", raw, 0)[0]
        sequence = struct.unpack_from("<Q", raw, 8)[0]
        if busy != 0:
            time.sleep(0.001)
            continue
        confirm = struct.unpack("<Q", read_exact(client, pid, data_address + 8, 8))[0]
        if confirm != sequence:
            time.sleep(0.001)
            continue
        names = {
            int(value["name_index"]): name
            for name, value in dict(state["functions"]).items()
        }
        name_index = struct.unpack_from("<I", raw, 0x68)[0]
        arg0, arg1 = struct.unpack_from("<ii", raw, 0x70)
        kind = raw[0x78]
        source, count, capacity, copied, row_size = struct.unpack_from("<Qiiii", raw, 0x80)
        process_event_caller, saved_rdi, saved_rsi = struct.unpack_from("<QQQ", raw, 0x98)
        receive_thunk_caller = struct.unpack_from("<Q", raw, 0xB0)[0]
        converter_caller = struct.unpack_from("<Q", raw, 0xB8)[0]
        rows: list[dict[str, int] | int] = []
        if 0 < copied <= MAX_ROWS:
            if kind == 1 and row_size == 4:
                rows = list(struct.unpack_from(f"<{copied}i", raw, ROWS_OFFSET))
            elif kind == 2 and row_size == BROKER_ROW_SIZE:
                for index in range(copied):
                    offset = ROWS_OFFSET + index * BROKER_ROW_SIZE
                    object_id, item_id, amount = struct.unpack_from("<i4xi4xq", raw, offset)
                    rows.append({"object_id": object_id, "item_id": item_id, "amount": amount})
        return {
            "sequence": sequence,
            "function_name_index": name_index,
            "function_name": names.get(name_index, ""),
            "arg0": arg0,
            "arg1": arg1,
            "process_event_caller": f"0x{process_event_caller:X}",
            "saved_rdi": f"0x{saved_rdi:X}",
            "saved_rsi": f"0x{saved_rsi:X}",
            "receive_thunk_caller": f"0x{receive_thunk_caller:X}",
            "converter_caller": f"0x{converter_caller:X}",
            "source_rows": f"0x{source:X}",
            "count": count,
            "capacity": capacity,
            "copied_count": copied,
            "row_size": row_size,
            "rows": rows,
        }
    raise RuntimeError("could not read a coherent broker capture")


def read_capture(client: Lu4MemoryClient, state: dict[str, object]) -> dict[str, object]:
    return read_capture_at(client, state, int(state["data"]))


def read_history(
    client: Lu4MemoryClient,
    state: dict[str, object],
    limit: int = HISTORY_DEPTH,
) -> list[dict[str, object]]:
    addresses = [int(value) for value in state.get("record_addresses", [state["data"]])]
    records = []
    for address in addresses[: max(1, min(limit, len(addresses)))]:
        record = read_capture_at(client, state, address)
        if int(record["sequence"]) <= 0:
            continue
        records.append(record)
    return records


def status() -> None:
    state = load_state()
    with Lu4MemoryClient() as client:
        result = read_capture(client, state)
        result["pid"] = int(state["pid"])
        result["patch_matches"] = (
            read_exact(client, int(state["pid"]), int(state["process_event"]), len(PROLOGUE)).hex()
            == str(state["patch_hex"])
        )
    print(json.dumps(result, indent=2))


def wait_capture(after: int, timeout: float) -> None:
    state = load_state()
    deadline = time.monotonic() + timeout
    with Lu4MemoryClient() as client:
        while time.monotonic() < deadline:
            result = read_capture(client, state)
            if int(result["sequence"]) > after:
                print(json.dumps(result, indent=2))
                return
            time.sleep(0.01)
    raise TimeoutError(f"no broker ProcessEvent after sequence {after}")


def uninstall() -> None:
    state = load_state()
    pid = int(state["pid"])
    original = bytes.fromhex(str(state["original_hex"]))
    patch = bytes.fromhex(str(state["patch_hex"]))
    with Lu4MemoryClient() as client:
        if read_exact(client, pid, int(state["process_event"]), len(original)) != patch:
            raise RuntimeError("ProcessEvent patch no longer matches broker state")
        patch_region(client, pid, int(state["process_event"]), original)
        client.free_process_memory(pid, int(state["cave"]))
        history_cave = int(state.get("history_cave", 0))
        if history_cave:
            client.free_process_memory(pid, history_cave)
        for allocation in state.get("history_caves", []):
            client.free_process_memory(pid, int(allocation["address"]))
    STATE_PATH.unlink()
    print("uninstalled ProcessEvent broker capture")


def main() -> int:
    parser = argparse.ArgumentParser(description="Capture LU4 ItemBroker ProcessEvent params")
    sub = parser.add_subparsers(dest="command", required=True)
    install_parser = sub.add_parser("install")
    install_parser.add_argument("pid", type=int)
    sub.add_parser("status")
    wait_parser = sub.add_parser("wait")
    wait_parser.add_argument("--after", type=int, default=0)
    wait_parser.add_argument("--timeout", type=float, default=30.0)
    sub.add_parser("uninstall")
    args = parser.parse_args()
    if args.command == "install":
        install(args.pid)
    elif args.command == "status":
        status()
    elif args.command == "wait":
        wait_capture(args.after, args.timeout)
    elif args.command == "uninstall":
        uninstall()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
