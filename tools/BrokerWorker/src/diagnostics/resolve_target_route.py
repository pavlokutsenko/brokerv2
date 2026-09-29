from __future__ import annotations

import argparse
import json
import pathlib
import struct
import sys


ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "client"))

from lu4_memory_client import Lu4MemoryClient  # noqa: E402
from scan_lu4_actors import Memory, pe_info
from discover_unreal_globals import scan_globals, validate_fname_pool
from inspect_shop_ufunctions import decode_name


MAX_CLASS_DEPTH = 32
MAX_CHILDREN = 4096
PROCESS_EVENT_VTABLE_INDEX = 77


def unpack(client: Lu4MemoryClient, pid: int, address: int, fmt: str) -> tuple:
    size = struct.calcsize(fmt)
    data = client.read(pid, address, size)
    if len(data) != size:
        raise RuntimeError(f"short read at 0x{address:X}: {len(data)}/{size}")
    return struct.unpack(fmt, data)


def u64(client: Lu4MemoryClient, pid: int, address: int) -> int:
    return unpack(client, pid, address, "<Q")[0]


def i32(client: Lu4MemoryClient, pid: int, address: int) -> int:
    return unpack(client, pid, address, "<i")[0]


def u16(client: Lu4MemoryClient, pid: int, address: int) -> int:
    return unpack(client, pid, address, "<H")[0]


def pointer(value: int) -> bool:
    return 0x10000 <= value <= 0x00007FFFFFFFFFFF


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("pid", type=int)
    parser.add_argument(
        "--snapshot",
        type=pathlib.Path,
        default=ROOT / "diagnostics" / "latest_actor_snapshot.json",
    )
    parser.add_argument("--json", type=pathlib.Path)
    parser.add_argument("--globals", type=pathlib.Path)
    args = parser.parse_args()

    snapshot = json.loads(args.snapshot.read_text(encoding="utf-8"))
    if snapshot["pid"] != args.pid:
        raise RuntimeError("snapshot PID does not match live PID")
    controller = int(snapshot["controller"], 16)
    module_base = int(snapshot["module_base"], 16)
    image_size = int(snapshot["image_size"], 16)

    result: dict[str, object] = {
        "pid": args.pid,
        "module_base": f"0x{module_base:X}",
        "controller": f"0x{controller:X}",
        "process_event": None,
        "select_target_candidates": [],
        "target_selected_order": None,
    }

    with Lu4MemoryClient() as client:
        memory = Memory(client, args.pid)
        if args.globals:
            tables = json.loads(args.globals.read_text(encoding="utf-8"))
            if tables.get("pid") != args.pid or len(tables.get("fname_pool_candidates", [])) != 1:
                raise RuntimeError("FNamePool report does not match the owned PID")
            pool = int(tables["fname_pool_candidates"][0]["address"])
            if validate_fname_pool(memory, pool, memory.read(pool, 0x30), 0) is None:
                raise RuntimeError("FNamePool report failed live validation")
        else:
            base = client.process_base(args.pid)
            _, names, _ = scan_globals(memory, base, pe_info(memory, base))
            if len(names) != 1:
                raise RuntimeError("FNamePool discovery is not unique")
            pool = names[0]["address"]
        vtable = u64(client, args.pid, controller)
        process_event = u64(
            client,
            args.pid,
            vtable + PROCESS_EVENT_VTABLE_INDEX * 8,
        )
        if not (module_base <= process_event < module_base + image_size):
            raise RuntimeError(f"ProcessEvent is outside image: 0x{process_event:X}")
        result["vtable"] = f"0x{vtable:X}"
        result["process_event"] = f"0x{process_event:X}"
        result["process_event_rva"] = f"0x{process_event - module_base:X}"

        owner = u64(client, args.pid, controller + 0x10)
        seen_functions: set[int] = set()
        candidates: list[dict[str, object]] = []
        order: dict[str, object] | None = None
        for depth in range(MAX_CLASS_DEPTH):
            if not pointer(owner):
                break
            child = u64(client, args.pid, owner + 0x48)
            for ordinal in range(MAX_CHILDREN):
                if not pointer(child) or child in seen_functions:
                    break
                seen_functions.add(child)
                function_owner = u64(client, args.pid, child + 0x20)
                name_index = i32(client, args.pid, child + 0x18)
                params_size = u16(client, args.pid, child + 0xB6)
                properties_size = i32(client, args.pid, child + 0x58)
                script_length = i32(client, args.pid, child + 0x68)
                entry = {
                    "address": f"0x{child:X}",
                    "owner": f"0x{function_owner:X}",
                    "owner_depth": depth,
                    "ordinal": ordinal,
                    "name_index": name_index,
                    "params_size": params_size,
                    "properties_size": properties_size,
                    "script_length": script_length,
                }
                if (
                    function_owner == owner
                    and params_size == 0x0A
                    and properties_size == 0x40
                    and 300 <= script_length <= 600
                ):
                    candidates.append(entry)
                if (
                    function_owner == owner
                    and decode_name(memory, pool, name_index) == "TargetSelected_Order"
                    and params_size == 0x24
                ):
                    order = entry
                next_child = u64(client, args.pid, child + 0x28)
                if next_child == child:
                    break
                child = next_child
            parent = u64(client, args.pid, owner + 0x40)
            if parent == owner:
                break
            owner = parent

        result["select_target_candidates"] = candidates
        result["target_selected_order"] = order

    rendered = json.dumps(result, ensure_ascii=False, indent=2)
    print(rendered)
    if args.json:
        args.json.write_text(rendered + "\n", encoding="utf-8")
    return 0 if len(candidates) == 1 and order is not None else 2


if __name__ == "__main__":
    raise SystemExit(main())
