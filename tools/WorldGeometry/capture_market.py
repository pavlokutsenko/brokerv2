"""Read loaded market geometry and visible traders; never call/write the client."""
import argparse
from collections import Counter
from datetime import datetime, timezone
import json
from pathlib import Path

from inspect_world import BUILD, GWORLD, GOBJECTS, Lu4MemoryClient, Memory, Survey, pe_info, pointer, read_object, validate_world_slot
from inspect_collision import Reflection
from market_components import component_tree, metadata, intersects
from read_mesh_triangles import extract


def capture(pid, extent):
    start = datetime.now(timezone.utc).isoformat()
    with Lu4MemoryClient() as client:
        base = client.process_base(pid)
        m = Memory(client, pid)
        pe = pe_info(m, base)
        if (pe["timestamp"], pe["image_size"]) != BUILD:
            raise RuntimeError("unsupported client build")
        s = Survey(m, base)
        if s.name(read_object(m, base + GOBJECTS, 0)) != "/Script/CoreUObject":
            raise RuntimeError("FName/GObjects guard failed")
        world = validate_world_slot(m, base + GWORLD, base, pe["image_size"])
        if not world or s.describe(world["world"])["class"] != "World":
            raise RuntimeError("world guard failed")
        levels = s.array(world["world"], 0x178, 512)
        if world["persistent_level"] not in levels:
            raise RuntimeError("persistent level absent")
        ref = Reflection(s)
        actors_seen, components_seen = set(), set()
        traders, instances, failures, skipped = [], [], [], Counter()
        assets, rejected = {}, {}
        for level in levels:
            if s.describe(level)["class"] != "Level":
                raise RuntimeError("level class guard failed")
            for actor in s.array(level, 0xA0, 65536):
                if not pointer(actor) or actor in actors_seen:
                    continue
                actors_seen.add(actor)
                if len(actors_seen) > 200000:
                    raise RuntimeError("actor budget exceeded")
                info = s.describe(actor)
                cls = info["class"]
                root = m.u64(actor + 0x1A0)
                if cls == "CharacterPlayer_C":
                    kiosk = m.i32(actor + 0x7DC)
                    if kiosk in (1, 3, 8) and pointer(root):
                        pos = s.position(root)
                        name = m.fstring(actor, 0x558, 32)
                        if pos and name:
                            traders.append({"name": name, "trader_key": name.strip().casefold(),
                                            "object_id": m.i32(actor + 0x550), "kind": kiosk,
                                            "position": pos, "actor": hex(actor)})
                    continue
                # Scene traversal includes Blueprint children, but not character rigs.
                if not pointer(root) or cls.startswith(("Controller", "Character", "Landscape")):
                    skipped[cls] += 1
                    continue
                try:
                    for component in component_tree(s, ref, root):
                        if component in components_seen:
                            continue
                        components_seen.add(component)
                        desc = s.describe(component)
                        if desc["class"] != "StaticMeshComponent":
                            if "Mesh" in desc["class"]:
                                skipped[desc["class"]] += 1
                            continue
                        item = metadata(s, ref, component)
                        if not intersects(item, extent):
                            continue
                        item.update(actor=info)
                        key = item["mesh"]["address"]
                        if key not in assets and key not in rejected:
                            try:
                                raw = extract(s, ref, {"root": desc, "name": info["name"]}, base)
                                assets[key] = {k: v for k, v in raw.items() if k not in ("world_vertices", "components", "transform", "actor")}
                            except (RuntimeError, KeyError, OSError) as error:
                                rejected[key] = str(error)
                        item["geometry_error"] = rejected.get(key)
                        instances.append(item)
                        if len(instances) > 1500:
                            raise RuntimeError("market mesh budget exceeded")
                except (RuntimeError, KeyError, OSError) as error:
                    failures.append({"actor": info, "error": str(error)})
        if m.u64(base + GWORLD) != world["world"]:
            raise RuntimeError("world changed during capture")
        final_position = s.position(world["player_capsule"])
    return {"pid": pid, "started_utc": start, "finished_utc": datetime.now(timezone.utc).isoformat(),
            "access": "read only; no client calls or writes", "extent": extent, "world": world,
            "player_end": final_position, "levels": len(levels), "actors": len(actors_seen),
            "traders": traders, "instances": instances, "assets": assets,
            "rejected_assets": rejected, "discovery_failures": failures, "skipped_classes": dict(skipped)}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("pid", type=int)
    p.add_argument("--extent", nargs=4, type=float, default=[79900, 84600, 146500, 150900])
    p.add_argument("--json", type=Path, required=True)
    args = p.parse_args()
    result = capture(args.pid, args.extent)
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(result, ensure_ascii=False, separators=(",", ":"))+"\n", encoding="utf-8")
    print(json.dumps({"file": str(args.json), "levels": result["levels"], "actors": result["actors"],
                      "traders": len(result["traders"]), "instances": len(result["instances"]),
                      "validated_assets": len(result["assets"]), "rejected": result["rejected_assets"],
                      "discovery_failures": result["discovery_failures"], "skipped": result["skipped_classes"]}, indent=2))


if __name__ == "__main__":
    main()
