from __future__ import annotations

import argparse
import json
import struct
import sys
from pathlib import Path


CLIENT_DIR = Path(__file__).resolve().parents[1] / "client"
sys.path.insert(0, str(CLIENT_DIR))

from lu4_memory_client import Lu4MemoryClient  # noqa: E402
from scan_lu4_actors import Memory, pointer  # noqa: E402


def decode_name(mem: Memory, pool: int, name_id: int) -> str:
    if name_id < 0:
        return ""
    block = (name_id & 0xFFFFFFFF) >> 16
    offset = name_id & 0xFFFF
    block_pointer = mem.u64(pool + 0x10 + block * 8)
    if not pointer(block_pointer):
        return ""
    entry = block_pointer + offset * 2
    header = mem.unpack("<H", entry, 0)
    length = header >> 6
    wide = bool(header & 1)
    if length <= 0 or length > 512:
        return ""
    try:
        raw = mem.page_read(entry + 2, length * (2 if wide else 1))
    except (OSError, RuntimeError):
        return ""
    return raw.decode("utf-16-le" if wide else "ascii", errors="replace")


def read_object(mem: Memory, gobjects: int, index: int) -> int:
    chunks = mem.u64(gobjects)
    count = mem.i32(gobjects + 0x14, -1)
    chunk_count = mem.i32(gobjects + 0x1C, -1)
    if not pointer(chunks) or not (0 <= index < count) or chunk_count <= 0:
        return 0
    chunk_index, within = divmod(index, 0x10000)
    if chunk_index >= chunk_count:
        return 0
    chunk = mem.u64(chunks + chunk_index * 8)
    return mem.u64(chunk + within * 0x18) if pointer(chunk) else 0


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Inspect the reflected shop/action UFunctions in current LU4"
    )
    parser.add_argument("pid", type=int)
    parser.add_argument("--globals", type=Path, required=True)
    parser.add_argument("--start", type=int, default=17500)
    parser.add_argument("--stop", type=int, default=20000)
    parser.add_argument(
        "--terms",
        nargs="+",
        default=("shop", "store", "market", "trader", "targetaction"),
    )
    parser.add_argument("--json", type=Path)
    args = parser.parse_args()

    globals_data = json.loads(args.globals.read_text(encoding="utf-8"))
    objects = globals_data.get("gobjects_candidates", [])
    names = globals_data.get("fname_pool_candidates", [])
    if len(objects) != 1 or len(names) != 1:
        raise RuntimeError("globals input must contain one GObjects and one FNamePool")
    gobjects = int(objects[0]["address"])
    pool = int(names[0]["address"])
    terms = tuple(term.casefold() for term in args.terms)
    found = []

    with Lu4MemoryClient() as client:
        mem = Memory(client, args.pid)
        count = mem.i32(gobjects + 0x14, -1)
        stop = min(args.stop, count)
        for index in range(max(0, args.start), stop):
            obj = read_object(mem, gobjects, index)
            if not pointer(obj):
                continue
            name_id = mem.i32(obj + 0x18, -1)
            name = decode_name(mem, pool, name_id)
            if not name or not any(term in name.casefold() for term in terms):
                continue
            outer = mem.u64(obj + 0x20)
            class_object = mem.u64(obj + 0x10)
            outer_name = (
                decode_name(mem, pool, mem.i32(outer + 0x18, -1))
                if pointer(outer)
                else ""
            )
            class_name = (
                decode_name(mem, pool, mem.i32(class_object + 0x18, -1))
                if pointer(class_object)
                else ""
            )
            found.append(
                {
                    "index": index,
                    "object": obj,
                    "name_id": name_id,
                    "name": name,
                    "outer": outer,
                    "outer_name": outer_name,
                    "class": class_object,
                    "class_name": class_name,
                    "function_flags": mem.unpack("<I", obj + 0xB0, 0),
                    "params_size": mem.unpack("<H", obj + 0xB6, 0),
                    "exec": mem.u64(obj + 0xD8),
                }
            )

    result = {
        "pid": args.pid,
        "gobjects": gobjects,
        "fname_pool": pool,
        "range": [args.start, stop],
        "matches": found,
    }
    rendered = json.dumps(result, ensure_ascii=False, indent=2)
    if args.json:
        args.json.write_text(rendered + "\n", encoding="utf-8")
    print(rendered)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
