"""Read-only, bounded discovery of loaded level geometry; never invokes the client."""
from __future__ import annotations

import argparse
from collections import Counter
from datetime import datetime, timezone
import json
import math
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
WORKER = ROOT / "tools/BrokerWorker/src"
if (Path(__file__).resolve().parent.parent / 'client').is_dir():
    WORKER = Path(__file__).resolve().parent.parent  # packaged worker payload
sys.path[:0] = [str(WORKER / "client"), str(WORKER / "diagnostics")]
from lu4_memory_client import Lu4MemoryClient
from scan_lu4_actors import Memory, pe_info, pointer, validate_world_slot
from inspect_shop_ufunctions import decode_name, read_object

# Current-build discovery profile. Fail closed on another executable.
BUILD = (0x956E0D97, 0xDCEB000)
GWORLD = 0x81F6A80
GOBJECTS = 0x80768E0
FNAMES = 0x7FBFB80


class Survey:
    def __init__(self, memory: Memory, base: int, pool_rva: int = FNAMES):
        self.m = memory
        self.pool = base + pool_rva
        self.names: dict[int, str] = {}

    def name(self, obj: int) -> str:
        if not pointer(obj):
            return ""
        ident = self.m.i32(obj + 0x18, -1)
        if ident not in self.names:
            self.names[ident] = decode_name(self.m, self.pool, ident)
        return self.names[ident]

    def describe(self, obj: int) -> dict:
        return {"address": hex(obj), "name": self.name(obj),
                "class": self.name(self.m.u64(obj + 0x10))}

    def path(self, obj: int) -> str:
        chain = []
        seen = set()
        for _ in range(10):
            if not pointer(obj) or obj in seen:
                break
            seen.add(obj)
            chain.append(self.name(obj))
            obj = self.m.u64(obj + 0x20)
        return ".".join(reversed(chain))

    def array(self, owner: int, offset: int, limit: int) -> list[int]:
        address = owner + offset
        for _ in range(3):
            header = self.m.read(address, 16)
            data, count, capacity = struct.unpack("<Qii", header)
            if count == 0:
                return []
            if not pointer(data) or not 0 < count <= limit or not count <= capacity <= limit * 4:
                raise RuntimeError(f"invalid bounded array at {address:#x}")
            values = self.m.read(data, count * 8)
            if self.m.read(address, 16) == header:
                return list(struct.unpack(f"<{count}Q", values))
        raise RuntimeError(f"changing array at {address:#x}")

    def position(self, component: int) -> list[float] | None:
        xyz = [self.m.f64(component + off) for off in (0x1F0, 0x1F8, 0x200)]
        return xyz if all(math.isfinite(v) and abs(v) < 1e8 for v in xyz) else None


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pid", type=int)
    parser.add_argument("--json", type=Path, required=True)
    args = parser.parse_args()
    with Lu4MemoryClient() as client:
        base = client.process_base(args.pid)
        mem = Memory(client, args.pid)
        pe = pe_info(mem, base)
        if (pe["timestamp"], pe["image_size"]) != BUILD:
            raise RuntimeError("unsupported client build")
        survey = Survey(mem, base)
        if survey.name(read_object(mem, base + GOBJECTS, 0)) != "/Script/CoreUObject":
            raise RuntimeError("GObjects/FName guard failed")
        world = validate_world_slot(mem, base + GWORLD, base, pe["image_size"])
        if not world or survey.describe(world["world"])["class"] != "World":
            raise RuntimeError("world guard failed")
        levels = survey.array(world["world"], 0x178, 512)
        if world["persistent_level"] not in levels:
            raise RuntimeError("loaded levels omit persistent level")
        classes = Counter()
        records, level_info, failures = [], [], []
        total = 0
        for level in levels:
            if survey.describe(level)["class"] != "Level":
                raise RuntimeError("loaded-level class guard failed")
            try:
                actors = survey.array(level, 0xA0, 65536)
            except RuntimeError as error:
                failures.append(str(error))
                continue
            total += len(actors)
            if total > 200000:
                raise RuntimeError("actor budget exceeded")
            level_info.append({**survey.describe(level), "path": survey.path(level), "actor_slots": len(actors)})
            for actor in actors:
                if not pointer(actor):
                    continue
                item = survey.describe(actor)
                classes[item["class"]] += 1
                if not any(term in (item["class"] + item["name"]).lower()
                           for term in ("mesh", "nav", "blocking", "landscape", "brush", "collision")):
                    continue
                root = mem.u64(actor + 0x1A0)
                item.update(level=hex(level), path=survey.path(actor), root=survey.describe(root))
                xyz = survey.position(root) if pointer(root) else None
                item["root_position_candidate"] = xyz
                item["distance_xy_candidate"] = math.hypot(xyz[0] - world["player"][0], xyz[1] - world["player"][1]) if xyz else None
                records.append(item)
        records.sort(key=lambda x: x["distance_xy_candidate"] if x["distance_xy_candidate"] is not None else float("inf"))
        functions = []
        for index in (4723, 248406):
            obj = read_object(mem, base + GOBJECTS, index)
            functions.append({"index": index, **survey.describe(obj), "outer": survey.name(mem.u64(obj + 0x20)),
                              "params_size": mem.unpack("<H", obj + 0xB6, 0), "exec": hex(mem.u64(obj + 0xD8))})
        report = {"captured_at_utc": datetime.now(timezone.utc).isoformat(), "pid": args.pid, "base": hex(base),
                  "access": "existing LU4Memory read-only IOCTLs", "world": world, "levels": level_info,
                  "actor_slots": total, "class_counts": dict(classes.most_common()), "geometry_actors": records,
                  "known_function_guards": functions, "incomplete_levels": failures}
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(json.dumps({"file": str(args.json), "levels": len(level_info), "actor_slots": total,
                          "class_counts": dict(classes.most_common(20)), "geometry_count": len(records),
                          "nearest": records[:6], "function_guards": functions, "incomplete_levels": failures}, ensure_ascii=True, indent=2))


if __name__ == "__main__":
    main()
