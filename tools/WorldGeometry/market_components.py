"""Bounded scene-component discovery and world bounds for market diagnostics."""
import itertools
import math
import struct

from inspect_world import pointer, decode_name
from triangle_geometry import transform


def component_tree(survey, reflection, root):
    pending, seen = [root], set()
    while pending:
        obj = pending.pop()
        if not pointer(obj) or obj in seen:
            continue
        seen.add(obj)
        if len(seen) > 128:
            raise RuntimeError("actor component budget exceeded")
        yield obj
        cls = survey.m.u64(obj + 0x10)
        offset = reflection.at(cls, "AttachChildren", 16)
        pending.extend(survey.array(obj, offset, 128))


def metadata(survey, reflection, component):
    m = survey.m
    cls = m.u64(component + 0x10)
    mesh = m.u64(component + reflection.at(cls, "StaticMesh", 8))
    if survey.describe(mesh)["class"] != "StaticMesh":
        raise RuntimeError("mesh class guard failed")
    mc = m.u64(mesh + 0x10)
    bounds = struct.unpack("<7d", m.read(mesh + reflection.at(mc, "ExtendedBounds", 56), 56))
    q = struct.unpack("<4d", m.read(component + 0x1D0, 32))
    p = struct.unpack("<3d", m.read(component + 0x1F0, 24))
    scale = struct.unpack("<3d", m.read(component + 0x210, 24))
    if not all(math.isfinite(v) and abs(v) < 1e8 for v in (*bounds, *p, *q, *scale)) or abs(sum(v*v for v in q)-1) > 1e-6:
        raise RuntimeError("invalid bounds/transform")
    corners = transform([[bounds[i] + sign[i]*bounds[i+3] for i in range(3)]
                         for sign in itertools.product((-1, 1), repeat=3)], p, q, scale)
    body = component + reflection.at(cls, "BodyInstance", 408)
    bt = reflection.struct_type(cls, "BodyInstance")
    enabled = m.unpack("<B", body + reflection.at(bt, "CollisionEnabled", 1), -1)
    profile = decode_name(m, survey.pool, m.i32(body + reflection.at(bt, "CollisionProfileName", 8), -1))
    return {"component": survey.describe(component), "mesh": survey.describe(mesh),
            "min": [min(v[i] for v in corners) for i in range(3)],
            "max": [max(v[i] for v in corners) for i in range(3)],
            "transform": {"position": p, "quaternion": q, "scale": scale},
            "collision_enabled": reflection.enum_value(bt, "CollisionEnabled", enabled),
            "collision_profile": profile}


def intersects(item, extent):
    return item["min"][0] <= extent[1] and item["max"][0] >= extent[0] and item["min"][1] <= extent[3] and item["max"][1] >= extent[2]
