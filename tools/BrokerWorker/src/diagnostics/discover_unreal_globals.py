from __future__ import annotations

import argparse
import json
import struct
import sys
import time
from pathlib import Path


CLIENT_DIR = Path(__file__).resolve().parents[1] / "client"
sys.path.insert(0, str(CLIENT_DIR))

from lu4_memory_client import Lu4MemoryClient  # noqa: E402
from scan_lu4_actors import Memory, pe_info, pointer  # noqa: E402
from worker_progress import check_stop


MAX_TRANSFER = 1024 * 1024
KNOWN_NAMES = {
    "None",
    "ByteProperty",
    "IntProperty",
    "BoolProperty",
    "FloatProperty",
    "ObjectProperty",
    "NameProperty",
    "DelegateProperty",
    "DoubleProperty",
    "ArrayProperty",
    "StructProperty",
}


def decode_fname_entry(mem: Memory, address: int) -> tuple[str, int] | None:
    header = mem.unpack("<H", address, None)
    if header is None:
        return None
    length = header >> 6
    wide = bool(header & 1)
    if length <= 0 or length > 512:
        return None
    width = 2 if wide else 1
    try:
        raw = mem.page_read(address + 2, length * width)
    except (OSError, RuntimeError):
        return None
    if wide:
        value = raw.decode("utf-16-le", errors="replace")
    else:
        value = raw.decode("ascii", errors="replace")
    size = 2 + length * width
    return value, size + (size & 1)


def validate_fname_pool(mem: Memory, slot: int, header: bytes, offset: int):
    current_block, cursor = struct.unpack_from("<ii", header, offset + 8)
    block0 = struct.unpack_from("<Q", header, offset + 0x10)[0]
    if not (1 <= current_block <= 4096 and 2 <= cursor <= 0x20000 and pointer(block0)):
        return None
    entry = block0
    known: list[str] = []
    first = None
    for index in range(64):
        decoded = decode_fname_entry(mem, entry)
        if decoded is None:
            break
        value, size = decoded
        if index == 0:
            first = value
            if value != "None":
                return None
        if value in KNOWN_NAMES:
            known.append(value)
        entry += size
    if len(set(known)) < 8:
        return None
    return {
        "address": slot,
        "current_block": current_block,
        "cursor": cursor,
        "block0": block0,
        "first": first,
        "known": sorted(set(known)),
    }


def validate_gobjects(mem: Memory, slot: int, header: bytes, offset: int):
    chunks = struct.unpack_from("<Q", header, offset)[0]
    max_elements, count, max_chunks, chunk_count = struct.unpack_from(
        "<iiii", header, offset + 0x10
    )
    if not pointer(chunks):
        return None
    if not (0 < count <= max_elements and 0 < chunk_count <= max_chunks <= 0x5FF):
        return None
    if (
        chunk_count > 64
        or count > chunk_count * 0x10000
        or max_elements != max_chunks * 0x10000
    ):
        return None
    try:
        chunk_ptrs = mem.page_read(chunks, chunk_count * 8)
    except (OSError, RuntimeError):
        return None
    chunks_list = list(struct.unpack(f"<{chunk_count}Q", chunk_ptrs))
    if not all(pointer(value) for value in chunks_list):
        return None
    first_object = mem.u64(chunks_list[0])
    # GC may leave any dynamic slot empty, including the first slot in the
    # final allocated chunk. It does not make the object array invalid.
    if not pointer(first_object):
        return None
    first_index = mem.i32(first_object + 0x0C, -1)
    if first_index != 0:
        return None
    for index in (1, 2):
        obj = mem.u64(chunks_list[0] + index * 0x18)
        if not pointer(obj) or mem.i32(obj + 0x0C, -1) != index:
            return None
    return {
        "address": slot,
        "chunks": chunks,
        "max_elements": max_elements,
        "count": count,
        "max_chunks": max_chunks,
        "chunk_count": chunk_count,
        "chunk_pointers": chunks_list,
        "first_object": first_object,
        "first_internal_index": first_index,
    }


def current_profile(mem, base, pe):
    """Validated module-relative globals for the supported build; no heap scan."""
    if (pe['timestamp'], pe['image_size']) != (0x956E0D97, 0xDCEB000):
        raise RuntimeError('Unsupported client build; globals discovery requires an explicit --scan diagnostic')
    slot = base + 0x80768E0
    objects = validate_gobjects(mem, slot, mem.read(slot, 0x30), 0)
    slot = base + 0x7FBFB80
    names = validate_fname_pool(mem, slot, mem.read(slot, 0x30), 0)
    if not objects or not names:
        raise RuntimeError('Known Unreal globals failed live structural validation')
    from inspect_shop_ufunctions import read_object, decode_name
    for index, expected in ((0, '/Script/CoreUObject'), (1, 'Object'), (2, 'Interface'), (218705, 'CharacterPlayer_C')):
        obj = read_object(mem, objects['address'], index)
        if not pointer(obj) or mem.i32(obj + 0x0C, -1) != index or decode_name(mem, names['address'], mem.i32(obj + 0x18, -1)) != expected:
            raise RuntimeError(f'Unreal object guard failed at index {index}')
    for row in (objects, names):
        row['rva'] = row['address'] - base
        row['section'] = '.data'
    return [objects], [names]


def scan_globals(mem: Memory, base: int, pe: dict):
    """Bounded, read-only fallback for a new image; callers require unique hits."""
    objects: list[dict] = []
    names: list[dict] = []
    checked = 0
    ranges = [
        section for section in pe["sections"]
        if section["characteristics"] & 0x40000000
        and section["characteristics"] & 0x80000000
    ]
    for section in ranges:
        section_start = base + int(section["rva"])
        remaining = int(section["size"])
        position = 0
        while position < remaining:
            check_stop()
            size = min(MAX_TRANSFER, remaining - position)
            data = mem.read(section_start + position, size)
            for offset in range(0, max(0, len(data) - 0x30), 8):
                checked += 1
                slot = section_start + position + offset
                chunks = struct.unpack_from("<Q", data, offset)[0]
                count = struct.unpack_from("<i", data, offset + 0x14)[0]
                chunk_count = struct.unpack_from("<i", data, offset + 0x1C)[0]
                if (len(objects) < 4 and pointer(chunks) and 1000 < count < 4_000_000
                        and 0 < chunk_count <= 64 and count <= chunk_count * 0x10000):
                    candidate = validate_gobjects(mem, slot, data, offset)
                    if candidate is not None:
                        candidate["rva"] = slot - base
                        candidate["section"] = section["name"]
                        objects.append(candidate)
                current_block = struct.unpack_from("<i", data, offset + 8)[0]
                cursor = struct.unpack_from("<i", data, offset + 0x0C)[0]
                block0 = struct.unpack_from("<Q", data, offset + 0x10)[0]
                if (len(names) < 4 and 1 <= current_block <= 4096
                        and 2 <= cursor <= 0x20000 and pointer(block0)):
                    candidate = validate_fname_pool(mem, slot, data, offset)
                    if candidate is not None:
                        candidate["rva"] = slot - base
                        candidate["section"] = section["name"]
                        names.append(candidate)
            position += size
    return objects, names, checked


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Read-only discovery of current LU4 GObjects and FNamePool"
    )
    parser.add_argument("pid", type=int)
    parser.add_argument("--json", type=Path)
    parser.add_argument("--scan", action="store_true", help="Explicit read-only lab discovery; not the normal collector path")
    args = parser.parse_args()

    started = time.perf_counter()
    with Lu4MemoryClient() as client:
        base = client.process_base(args.pid)
        mem = Memory(client, args.pid)
        pe = pe_info(mem, base)
        if args.scan or (pe["timestamp"], pe["image_size"]) != (0x956E0D97, 0xDCEB000):
            objects, names, checked = scan_globals(mem, base, pe)
        else:
            objects, names = current_profile(mem, base, pe)
            checked = 0

    result = {
        "pid": args.pid,
        "module_base": base,
        "timestamp": pe["timestamp"],
        "image_size": pe["image_size"],
        "checked_slots": checked,
        "elapsed_ms": round((time.perf_counter() - started) * 1000, 3),
        "gobjects_candidates": objects,
        "fname_pool_candidates": names,
    }
    rendered = json.dumps(result, ensure_ascii=False, indent=2)
    if args.json:
        args.json.write_text(rendered + "\n", encoding="utf-8")
    print(rendered)
    return 0 if len(objects) == 1 and len(names) == 1 else 2


if __name__ == "__main__":
    raise SystemExit(main())
