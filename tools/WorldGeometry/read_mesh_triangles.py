"""Read and cross-check BodySetup triangle geometry; no client calls or writes."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import struct

from inspect_world import BUILD, GWORLD, Lu4MemoryClient, Memory, Survey, pe_info, pointer
from inspect_collision import Reflection
from triangle_geometry import components, transform, triangle_multiset

# Native offsets verified only against this executable and four control assets.
TRIANGLE_VTABLE_RVA = 0x63E7A58


def array(memory, address, fmt, limit):
    header = memory.read(address, 16)
    data, count, capacity = struct.unpack("<Qii", header)
    if not pointer(data) or not 0 < count <= limit or not count <= capacity <= limit * 4:
        raise RuntimeError(f"invalid bounded array at {address:#x}")
    raw = memory.read(data, count * struct.calcsize(fmt))
    if memory.read(address, 16) != header or memory.read(data, len(raw)) != raw:
        raise RuntimeError("geometry changed during capture")
    return list(struct.iter_unpack(fmt, raw)), hashlib.sha256(raw).hexdigest()


def extract(survey, reflection, actor, base):
    m = survey.m
    root = int(actor["root"]["address"], 16)
    if survey.describe(root)["class"] != "StaticMeshComponent":
        raise RuntimeError("component class changed")
    mesh = m.u64(root + reflection.at(m.u64(root + 0x10), "StaticMesh", 8))
    if survey.describe(mesh)["class"] != "StaticMesh":
        raise RuntimeError("mesh class guard failed")
    mesh_class = m.u64(mesh + 0x10)
    body = m.u64(mesh + reflection.at(mesh_class, "BodySetup", 8))
    if survey.describe(body)["class"] != "BodySetup":
        raise RuntimeError("BodySetup class guard failed")
    body_class = m.u64(body + 0x10)
    build_scale = struct.unpack("<3d", m.read(body + reflection.at(body_class, "BuildScale3D", 24), 24))
    if build_scale != (1.0, 1.0, 1.0):
        raise RuntimeError("non-unit BodySetup build scale is not validated")
    native_refs, refs_hash = array(m, body + 0xF8, "<Q", 64)
    if len(native_refs) != 1:
        raise RuntimeError("expected one native triangle object")
    native = native_refs[0][0]
    if not pointer(native) or m.u64(native) != base + TRIANGLE_VTABLE_RVA:
        raise RuntimeError("native triangle object vtable guard failed")
    vertices, vertex_hash = array(m, native + 0x48, "<3f", 80000)
    triangles, triangle_hash = array(m, native + 0x70, "<3H", 150000)
    aux_vertices, aux_vertex_hash = array(m, body + 0x118, "<3d", 40000)
    aux_indices, aux_index_hash = array(m, body + 0x108, "<i", 150000)
    if len(aux_indices) % 3:
        raise RuntimeError("auxiliary index count is not a triangle list")
    aux_faces = [tuple(aux_indices[i + j][0] for j in range(3)) for i in range(0, len(aux_indices), 3)]
    for points, faces in ((vertices, triangles), (aux_vertices, aux_faces)):
        if not all(math.isfinite(v) and abs(v) < 1e8 for p in points for v in p):
            raise RuntimeError("invalid vertex coordinates")
        if not all(0 <= i < len(points) for face in faces for i in face):
            raise RuntimeError("triangle index out of range")
    if triangle_multiset(vertices, triangles) != triangle_multiset(aux_vertices, aux_faces):
        raise RuntimeError("native and auxiliary triangle coordinate multisets differ")
    bounds = struct.unpack("<7d", m.read(mesh + reflection.at(mesh_class, "ExtendedBounds", 56), 56))
    for axis in range(3):
        if min(v[axis] for v in vertices) < bounds[axis] - bounds[axis + 3] - 0.02 or max(v[axis] for v in vertices) > bounds[axis] + bounds[axis + 3] + 0.02:
            raise RuntimeError("triangle geometry exceeds asset bounds")
    quaternion = struct.unpack("<4d", m.read(root + 0x1D0, 32))
    position = struct.unpack("<3d", m.read(root + 0x1F0, 24))
    scale = struct.unpack("<3d", m.read(root + 0x210, 24))
    if not all(math.isfinite(v) for v in (*position, *quaternion, *scale)) or abs(sum(v * v for v in quaternion) - 1) > 1e-6:
        raise RuntimeError("invalid component transform")
    world_vertices = transform(vertices, position, quaternion, scale)
    return {"actor": actor["name"], "mesh": survey.describe(mesh), "body_setup": hex(body),
            "native_object": hex(native), "vertices": vertices, "triangles": triangles,
            "world_vertices": world_vertices, "components": components(world_vertices, triangles),
            "transform": {"position": position, "quaternion": quaternion, "scale": scale},
            "aux_vertex_count": len(aux_vertices), "exact_triangle_multiset_match": True,
            "hashes": {"refs": refs_hash, "vertices": vertex_hash, "triangles": triangle_hash,
                       "aux_vertices": aux_vertex_hash, "aux_indices": aux_index_hash}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--world-json", type=Path, required=True)
    parser.add_argument("--names", nargs="+", default=["Giran_V_Plaza_Stair01", "Giran_V_Plaza_Elevation", "Giran_AdenaGirl_Btm", "Giran_V_Plaza_Pole6"])
    parser.add_argument("--json", type=Path, required=True)
    args = parser.parse_args()
    world = json.loads(args.world_json.read_text(encoding="utf-8"))
    with Lu4MemoryClient() as client:
        base = client.process_base(world["pid"])
        m = Memory(client, world["pid"])
        pe = pe_info(m, base)
        if (pe["timestamp"], pe["image_size"]) != BUILD or m.u64(base + GWORLD) != world["world"]["world"]:
            raise RuntimeError("world/build changed; recapture actors")
        survey = Survey(m, base)
        reflection = Reflection(survey)
        records = []
        for name in args.names:
            found = [a for a in world["geometry_actors"] if a["name"] == name and a["class"] == "StaticMeshActor"]
            if len(found) != 1:
                raise RuntimeError(f"expected unique StaticMeshActor: {name}")
            records.append(extract(survey, reflection, found[0], base))
    result = {"pid": world["pid"], "captured_at_utc": datetime.now(timezone.utc).isoformat(),
              "access": "read only; no ProcessEvent or hooks", "records": records}
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(result, separators=(",", ":")) + "\n", encoding="utf-8")
    print(json.dumps({"file": str(args.json), "records": [{"name": r["actor"], "vertices": len(r["vertices"]),
          "triangles": len(r["triangles"]), "components": len(r["components"]), "exact_match": r["exact_triangle_multiset_match"]} for r in records]}, indent=2))


if __name__ == "__main__":
    main()
