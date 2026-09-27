"""Inspect reflected collision metadata of mesh instances, without client calls."""
from __future__ import annotations

import argparse
from collections import Counter
import json
from pathlib import Path
import struct

from inspect_world import BUILD, GWORLD, Lu4MemoryClient, Memory, Survey, decode_name, pe_info, pointer


class Reflection:
    def __init__(self, survey: Survey):
        self.s = survey
        self.m = survey.m
        self.cache: dict[int, dict] = {}

    def fields(self, owner: int) -> dict:
        if owner in self.cache:
            return self.cache[owner]
        result = {}
        current = owner
        supers = set()
        while pointer(current) and current not in supers and len(supers) < 16:
            supers.add(current)
            field = self.m.u64(current + 0x50)
            visited = set()
            while pointer(field) and field not in visited and len(visited) < 256:
                visited.add(field)
                name = decode_name(self.m, self.s.pool, self.m.i32(field + 0x20, -1))
                offset, size = self.m.i32(field + 0x44, -1), self.m.i32(field + 0x34, -1)
                if not name or not name.isprintable() or not 0 <= offset < 0x10000 or not 0 < size < 0x10000:
                    raise RuntimeError("FProperty layout guard failed")
                result.setdefault(name, {"field": field, "offset": offset, "size": size})
                field = self.m.u64(field + 0x18)
            current = self.m.u64(current + 0x40)
        self.cache[owner] = result
        return result

    def at(self, owner: int, name: str, size: int) -> int:
        item = self.fields(owner)[name]
        if item["size"] != size:
            raise RuntimeError(f"property size mismatch: {name}")
        return item["offset"]

    def struct_type(self, owner: int, name: str) -> int:
        result = self.m.u64(self.fields(owner)[name]["field"] + 0x70)
        if self.s.describe(result)["class"] != "ScriptStruct":
            raise RuntimeError(f"struct property type mismatch: {name}")
        return result

    def enum_value(self, owner: int, name: str, value: int) -> str:
        enum = self.m.u64(self.fields(owner)[name]["field"] + 0x70)
        if self.s.describe(enum)["class"] != "Enum":
            raise RuntimeError(f"enum property type mismatch: {name}")
        data, count, capacity = struct.unpack("<Qii", self.m.read(enum + 0x40, 16))
        if not pointer(data) or not 0 < count <= capacity <= 128:
            raise RuntimeError("enum array guard failed")
        for index in range(count):
            entry = data + index * 16
            if self.m.unpack("<q", entry + 8, -1) == value:
                return decode_name(self.m, self.s.pool, self.m.i32(entry, -1))
        raise RuntimeError(f"unknown enum value: {name}={value}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--world-json", type=Path, required=True)
    parser.add_argument("--json", type=Path, required=True)
    parser.add_argument("--radius", type=float, default=3000)
    args = parser.parse_args()
    world = json.loads(args.world_json.read_text(encoding="utf-8"))
    with Lu4MemoryClient() as client:
        base = client.process_base(world["pid"])
        m = Memory(client, world["pid"])
        pe = pe_info(m, base)
        if (pe["timestamp"], pe["image_size"]) != BUILD or m.u64(base + GWORLD) != world["world"]["world"]:
            raise RuntimeError("world/build changed; recapture actors")
        s = Survey(m, base)
        reflection = Reflection(s)
        rows, failures = [], []
        selected = [a for a in world["geometry_actors"] if a["class"] == "StaticMeshActor"
                    and a["distance_xy_candidate"] is not None and a["distance_xy_candidate"] <= args.radius]
        if len(selected) > 500:
            raise RuntimeError("local mesh budget exceeded")
        for actor in selected:
            try:
                root = int(actor["root"]["address"], 16)
                cls = m.u64(root + 0x10)
                if s.name(cls) != "StaticMeshComponent":
                    raise RuntimeError("component class changed")
                mesh = m.u64(root + reflection.at(cls, "StaticMesh", 8))
                mesh_cls = m.u64(mesh + 0x10)
                if s.name(mesh_cls) != "StaticMesh":
                    raise RuntimeError("mesh class guard failed")
                body = root + reflection.at(cls, "BodyInstance", 408)
                body_type = reflection.struct_type(cls, "BodyInstance")
                setup = m.u64(mesh + reflection.at(mesh_cls, "BodySetup", 8))
                setup_cls = m.u64(setup + 0x10)
                if s.name(setup_cls) != "BodySetup":
                    raise RuntimeError("BodySetup class guard failed")
                profile = decode_name(m, s.pool, m.i32(body + reflection.at(body_type, "CollisionProfileName", 8), -1))
                enabled = m.unpack("<B", body + reflection.at(body_type, "CollisionEnabled", 1), -1)
                trace_flag = m.unpack("<B", setup + reflection.at(setup_cls, "CollisionTraceFlag", 1), -1)
                agg = setup + reflection.fields(setup_cls)["AggGeom"]["offset"]
                agg_type = reflection.struct_type(setup_cls, "AggGeom")
                shapes = {}
                for name, prop in reflection.fields(agg_type).items():
                    if not name.endswith("Elems") or prop["size"] != 16:
                        continue
                    data, count, capacity = struct.unpack("<Qii", m.read(agg + prop["offset"], 16))
                    if not 0 <= count <= capacity <= 65536 or (count and not pointer(data)):
                        raise RuntimeError("collision shape array guard failed")
                    shapes[name] = count
                bounds = list(struct.unpack("<7d", m.read(mesh + reflection.at(mesh_cls, "ExtendedBounds", 56), 56)))
                relative = list(struct.unpack("<3d", m.read(root + reflection.at(cls, "RelativeLocation", 24), 24)))
                nav = m.u64(mesh + reflection.at(mesh_cls, "NavCollision", 8))
                rows.append({"actor": actor["name"], "actor_address": actor["address"], "mesh": s.describe(mesh),
                             "position_candidate": actor["root_position_candidate"], "relative_location": relative,
                             "distance_xy_candidate": actor["distance_xy_candidate"], "collision_profile": profile,
                             "collision_enabled_raw": enabled, "body_setup": s.describe(setup),
                             "collision_enabled": reflection.enum_value(body_type, "CollisionEnabled", enabled),
                             "collision_trace_flag_raw": trace_flag,
                             "collision_trace_flag": reflection.enum_value(setup_cls, "CollisionTraceFlag", trace_flag),
                             "simple_shape_counts": shapes, "asset_local_extended_bounds_7d": bounds,
                             "nav_collision": s.describe(nav) if pointer(nav) else None})
            except (KeyError, RuntimeError, OSError) as error:
                failures.append({"actor": actor["name"], "error": str(error)})
        metadata = [{"owner": s.describe(owner), "fields": fields} for owner, fields in reflection.cache.items()]
        result = {"pid": world["pid"], "world_snapshot": str(args.world_json), "radius": args.radius,
                  "mesh_count": len(rows), "rows": rows, "failures": failures, "reflection": metadata,
                  "warning": "Metadata only: not a collision query or a verified walkability map; bounds are asset-local."}
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        print(json.dumps({"file": str(args.json), "mesh_count": len(rows), "profiles": dict(Counter(x["collision_profile"] for x in rows)),
                          "enabled_modes": dict(Counter(x["collision_enabled"] for x in rows)),
                          "trace_modes": dict(Counter(x["collision_trace_flag"] for x in rows)),
                          "with_nav_collision": sum(bool(x["nav_collision"]) for x in rows),
                          "samples": rows[:3], "failures": failures}, indent=2))


if __name__ == "__main__":
    main()
