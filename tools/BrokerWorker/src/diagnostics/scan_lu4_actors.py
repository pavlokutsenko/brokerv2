from __future__ import annotations

import argparse
import json
import math
import struct
import sys
import unicodedata
from pathlib import Path


CLIENT_DIR = Path(__file__).resolve().parents[1] / "client"
sys.path.insert(0, str(CLIENT_DIR))

from lu4_memory_client import Lu4MemoryClient  # noqa: E402


KNOWN_GWORLD_RVAS = (0x08201A80, 0x081F2400)
MAX_ACTORS = 65536
MAX_TRANSFER = 1024 * 1024


def pointer(value: int) -> bool:
    return 0x10000 <= value < 0x0000800000000000


class Memory:
    def __init__(self, client: Lu4MemoryClient, pid: int) -> None:
        self.client = client
        self.pid = pid
        self.pages: dict[int, bytes] = {}

    def read(self, address: int, size: int) -> bytes:
        data = self.client.read(self.pid, address, size)
        if len(data) != size:
            raise RuntimeError(
                f"short read at 0x{address:X}: requested={size} copied={len(data)}"
            )
        return data

    def page_read(self, address: int, size: int) -> bytes:
        if size <= 0:
            return b""
        first = address & ~0xFFF
        last = (address + size - 1) & ~0xFFF
        chunks = []
        page = first
        while page <= last:
            if page not in self.pages:
                self.pages[page] = self.read(page, 0x1000)
            chunks.append(self.pages[page])
            page += 0x1000
        joined = b"".join(chunks)
        offset = address - first
        return joined[offset : offset + size]

    def unpack(self, fmt: str, address: int, default=None):
        size = struct.calcsize(fmt)
        try:
            return struct.unpack(fmt, self.page_read(address, size))[0]
        except (OSError, RuntimeError, struct.error):
            return default

    def u64(self, address: int, default: int = 0) -> int:
        return self.unpack("<Q", address, default)

    def i32(self, address: int, default: int = 0) -> int:
        return self.unpack("<i", address, default)

    def f64(self, address: int, default: float = math.nan) -> float:
        return self.unpack("<d", address, default)

    def fstring(self, obj: int, offset: int, max_chars: int) -> str:
        data = self.u64(obj + offset)
        length = self.i32(obj + offset + 8)
        capacity = self.i32(obj + offset + 12)
        if (
            not pointer(data)
            or length <= 0
            or length > max_chars
            or capacity < length
            or capacity > max_chars * 2
        ):
            return ""
        try:
            raw = self.page_read(data, length * 2)
            return raw.decode("utf-16-le", errors="replace").rstrip("\0")
        except (OSError, RuntimeError):
            return ""


def pe_info(mem: Memory, base: int) -> dict:
    dos = mem.read(base, 0x1000)
    if dos[:2] != b"MZ":
        raise RuntimeError("module has no MZ signature")
    e_lfanew = struct.unpack_from("<I", dos, 0x3C)[0]
    header = mem.read(base + e_lfanew, 0x108)
    if header[:4] != b"PE\0\0":
        raise RuntimeError("module has no PE signature")
    sections = struct.unpack_from("<H", header, 6)[0]
    timestamp = struct.unpack_from("<I", header, 8)[0]
    optional_size = struct.unpack_from("<H", header, 20)[0]
    image_size = struct.unpack_from("<I", header, 24 + 56)[0]
    table_address = base + e_lfanew + 24 + optional_size
    table = mem.read(table_address, sections * 40)
    parsed = []
    for index in range(sections):
        off = index * 40
        name = table[off : off + 8].split(b"\0", 1)[0].decode("ascii", "replace")
        virtual_size, rva, raw_size = struct.unpack_from("<III", table, off + 8)
        characteristics = struct.unpack_from("<I", table, off + 36)[0]
        parsed.append(
            {
                "name": name,
                "rva": rva,
                "size": max(virtual_size, raw_size),
                "characteristics": characteristics,
            }
        )
    return {
        "timestamp": timestamp,
        "image_size": image_size,
        "sections": parsed,
    }


def finite_coordinate(value: float) -> bool:
    return math.isfinite(value) and abs(value) < 1_000_000_000.0


def display_name(value: str) -> bool:
    value = value.strip()
    return len(value) >= 2 and all(
        unicodedata.category(character) not in ("Cc", "Cs", "Co")
        for character in value
    )


def validate_world_slot(mem: Memory, slot: int, base: int, image_size: int):
    world = mem.u64(slot)
    if not pointer(world):
        return None
    level = mem.u64(world + 0x30)
    game_instance = mem.u64(world + 0x1D8)
    if not pointer(level) or not pointer(game_instance):
        return None
    actors = mem.u64(level + 0xA0)
    actor_count = mem.i32(level + 0xA8, -1)
    if not pointer(actors) or not 0 < actor_count <= MAX_ACTORS:
        return None
    local_players = mem.u64(game_instance + 0x38)
    local_player = mem.u64(local_players) if pointer(local_players) else 0
    controller = mem.u64(local_player + 0x30) if pointer(local_player) else 0
    player_actor = mem.u64(controller + 0x2D0) if pointer(controller) else 0
    capsule = mem.u64(player_actor + 0x1A0) if pointer(player_actor) else 0
    if not all(pointer(v) for v in (local_player, controller, player_actor, capsule)):
        return None
    x = mem.f64(capsule + 0x1F0)
    y = mem.f64(capsule + 0x1F8)
    z = mem.f64(capsule + 0x200)
    vtable = mem.u64(controller)
    process_event = mem.u64(vtable + 77 * 8) if pointer(vtable) else 0
    if (
        not all(finite_coordinate(v) for v in (x, y, z))
        or not base <= process_event < base + image_size
    ):
        return None
    return {
        "slot": slot,
        "gworld_rva": slot - base,
        "world": world,
        "persistent_level": level,
        "game_instance": game_instance,
        "controller": controller,
        "player_actor": player_actor,
        "player_capsule": capsule,
        "actors_data": actors,
        "actor_count": actor_count,
        "player": (x, y, z),
        "process_event": process_event,
    }


def scan_world(mem: Memory, base: int, pe: dict):
    for rva in KNOWN_GWORLD_RVAS:
        candidate = validate_world_slot(mem, base + rva, base, pe["image_size"])
        if candidate:
            candidate["source"] = "known_profile"
            return candidate

    writable = [
        section
        for section in pe["sections"]
        if section["characteristics"] & 0x40000000
        and section["characteristics"] & 0x80000000
    ]
    checked = 0
    for section in writable:
        start = base + section["rva"]
        stop = start + section["size"]
        address = start
        while address + 8 <= stop:
            size = min(MAX_TRANSFER, stop - address)
            size -= size % 8
            if size == 0:
                break
            try:
                data = mem.read(address, size)
            except (OSError, RuntimeError):
                address += size
                continue
            for offset in range(0, len(data) - 7, 8):
                value = struct.unpack_from("<Q", data, offset)[0]
                checked += 1
                if not pointer(value):
                    continue
                candidate = validate_world_slot(
                    mem, address + offset, base, pe["image_size"]
                )
                if candidate:
                    candidate["source"] = "writable_section_scan"
                    candidate["checked_slots"] = checked
                    return candidate
            address += size
    return None


def coherent_actor_snapshot(mem: Memory, level: int):
    header_address = level + 0xA0
    for attempt in range(1, 4):
        first = mem.read(header_address, 16)
        data, count, capacity = struct.unpack("<Qii", first)
        if not pointer(data) or not 0 < count <= MAX_ACTORS or capacity < count:
            raise RuntimeError(
                f"invalid actor header data=0x{data:X} count={count} capacity={capacity}"
            )
        raw = mem.read(data, count * 8)
        second = mem.read(header_address, 16)
        if first == second:
            return list(struct.unpack(f"<{count}Q", raw)), attempt
    raise RuntimeError("actor snapshot remained incoherent after 3 attempts")


def enumerate_positioned_actors(mem: Memory, world: dict, actors: list[int]):
    px, py, pz = world["player"]
    found = []
    for index, actor in enumerate(actors):
        if not pointer(actor):
            continue
        capsule = mem.u64(actor + 0x1A0)
        if not pointer(capsule):
            continue
        x = mem.f64(capsule + 0x1F0)
        y = mem.f64(capsule + 0x1F8)
        z = mem.f64(capsule + 0x200)
        if not all(finite_coordinate(v) for v in (x, y, z)):
            continue
        distance = math.hypot(x - px, y - py)
        found.append(
            {
                "slot": index + 1,
                "actor": f"0x{actor:X}",
                "object_id": mem.i32(actor + 0x550),
                "name": mem.fstring(actor, 0x558, 32),
                "kiosk_type": mem.i32(actor + 0x7DC),
                "sell_title": mem.fstring(actor, 0x810, 128),
                "buy_title": mem.fstring(actor, 0x820, 128),
                "x": x,
                "y": y,
                "z": z,
                "distance": distance,
                "is_player": actor == world["player_actor"],
            }
        )
    found.sort(key=lambda item: item["distance"])
    return found


def main() -> int:
    parser = argparse.ArgumentParser(description="Read LU4 actor state through LU4Memory")
    parser.add_argument("pid", type=int)
    parser.add_argument("--limit", type=int, default=40)
    parser.add_argument("--json", type=Path)
    args = parser.parse_args()

    with Lu4MemoryClient() as client:
        mem = Memory(client, args.pid)
        base = client.process_base(args.pid)
        pe = pe_info(mem, base)
        world = scan_world(mem, base, pe)
        if not world:
            raise RuntimeError("no validated GWorld slot found")
        actors, attempts = coherent_actor_snapshot(mem, world["persistent_level"])
        positioned = enumerate_positioned_actors(mem, world, actors)
        named = [item for item in positioned if display_name(item["name"])]
        traders = [
            item
            for item in positioned
            if item["name"]
            and item["kiosk_type"] in (1, 3, 8)
        ]
        result = {
            "pid": args.pid,
            "module_base": f"0x{base:X}",
            "pe_timestamp": f"0x{pe['timestamp']:08X}",
            "image_size": f"0x{pe['image_size']:X}",
            "gworld_rva": f"0x{world['gworld_rva']:X}",
            "gworld_source": world["source"],
            "world": f"0x{world['world']:X}",
            "persistent_level": f"0x{world['persistent_level']:X}",
            "controller": f"0x{world['controller']:X}",
            "player_actor": f"0x{world['player_actor']:X}",
            "player": {"x": world["player"][0], "y": world["player"][1], "z": world["player"][2]},
            "actor_count": len(actors),
            "snapshot_attempts": attempts,
            "positioned_actor_count": len(positioned),
            "nearest": positioned[: max(0, args.limit)],
            "named_actor_count": len(named),
            "named": named,
            "trader_count": len(traders),
            "traders": traders,
        }
        encoded = json.dumps(result, ensure_ascii=False, indent=2)
        if args.json:
            args.json.parent.mkdir(parents=True, exist_ok=True)
            args.json.write_text(encoded + "\n", encoding="utf-8")
        try:
            print(encoded)
        except UnicodeEncodeError:
            sys.stdout.buffer.write((encoded + "\n").encode("utf-8", errors="replace"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
