"""Offline diagram from validated triangles; sections are not a navigation map.

Requires the already available Shapely package only for offline polygon union.
The section plane is 45 units above an explicitly approximate plaza floor:
z=-3495 at x<=83095, rising to -3410 at x=83481, then constant.
"""
import argparse
from collections import Counter
import json
from pathlib import Path

from shapely import set_precision
from shapely.geometry import LineString, Polygon, box
from shapely.ops import polygonize, unary_union

from triangle_geometry import components, transform

BREAKS = (-1e9, 83095, 83481, 1e9)


def plane(x):
    return -3495 + 85 * max(0, min(1, (x-83095)/386)) + 45


def clip_x(poly, boundary, lower):
    result = []
    for a, b in zip(poly, poly[1:]+poly[:1]):
        ina, inb = (a[0] >= boundary, b[0] >= boundary) if lower else (a[0] <= boundary, b[0] <= boundary)
        if ina:
            result.append(a)
        if ina != inb:
            t = (boundary-a[0])/(b[0]-a[0])
            result.append([a[i]+t*(b[i]-a[i]) for i in range(3)])
    return result


def section(vertices, faces):
    lines = set()
    for face in faces:
        triangle = [vertices[i] for i in face]
        for lo, hi in zip(BREAKS, BREAKS[1:]):
            poly = clip_x(clip_x(triangle, lo, True), hi, False)
            points = []
            for a, b in zip(poly, poly[1:]+poly[:1]):
                da, db = a[2]-plane(a[0]), b[2]-plane(b[0])
                if abs(da) < 1e-7:
                    points.append(tuple(round(v, 3) for v in a[:2]))
                if da*db < 0:
                    t = da/(da-db)
                    points.append(tuple(round(a[i]+t*(b[i]-a[i]), 3) for i in (0, 1)))
            points = list(dict.fromkeys(points))
            if len(points) == 2 and points[0] != points[1]:
                lines.add(tuple(sorted(points)))
    if not lines:
        return Polygon()
    merged = unary_union([set_precision(LineString(line), .01) for line in lines])
    filled = []
    for poly in polygonize(merged):
        p = poly.representative_point()
        crossings = 0
        for a, b in lines:
            if (a[1] > p.y) != (b[1] > p.y):
                crossing = a[0] + (p.y-a[1])*(b[0]-a[0])/(b[1]-a[1])
                crossings += crossing > p.x
        if crossings % 2:
            filled.append(poly)
    # Preserve open collision surfaces as thin marks, not filled invented hulls.
    return unary_union([*filled, merged.buffer(.65, cap_style=2)])


def rings(geometry, crop):
    result = []
    geom = geometry.intersection(crop).simplify(.6, preserve_topology=True)
    if geom.is_empty:
        return result
    polygons = [geom] if geom.geom_type == "Polygon" else list(getattr(geom, "geoms", []))
    for poly in polygons:
        if poly.geom_type != "Polygon" or poly.area < .2:
            continue
        for ring in [poly.exterior, *poly.interiors]:
            result.append([[round(x-80000, 1), round(y-147000, 1)] for x, y in ring.coords])
    return result


def build(raw, extent=None):
    extent = extent or [80350, 84600, 146950, 150000]
    crop = box(extent[0], extent[2], extent[1], extent[3])
    obstacles, floors, unknown = [], [], []
    asset_groups = {}
    for item in raw["instances"]:
        if item["collision_enabled"].endswith("NoCollision"):
            continue
        key = item["mesh"]["address"]
        if item["geometry_error"]:
            geom = box(item["min"][0], item["min"][1], item["max"][0], item["max"][1])
            unknown.append({"name": item["actor"]["name"], "rings": rings(geom, crop)})
            continue
        asset = raw["assets"][key]
        vertices = transform(asset["vertices"], **item["transform"])
        name = item["actor"]["name"]
        if key not in asset_groups:
            asset_groups[key] = components(asset["vertices"], asset["triangles"])
        sections = [section(vertices, [asset["triangles"][i] for i in group["triangle_indices"]])
                    for group in asset_groups[key]]
        contours = rings(unary_union(sections), crop)
        if contours:
            obstacles.append({"name": name, "mesh": item["mesh"]["name"], "rings": contours,
                              "triangles": len(asset["triangles"]), "component": item["component"]["address"]})
        if any(word in item["mesh"]["name"] for word in ("Floor", "Stair", "Elevation")):
            triangles = []
            for face in asset["triangles"]:
                v = [vertices[i] for i in face]
                if not -3565 < sum(p[2] for p in v)/3 < -3375:
                    continue
                poly = Polygon([p[:2] for p in v])
                if poly.area > .1:
                    triangles.append(poly)
            if triangles:
                contours = rings(unary_union(triangles), crop)
                if contours:
                    floors.append({"name": name, "stairs": "Stair" in name, "rings": contours})
    traders = [[t["name"], round(t["position"][0]-80000, 1), round(t["position"][1]-147000, 1),
                round(t["position"][2], 1), t["kind"], t["trader_key"]] for t in raw["traders"]
               if extent[0] <= t["position"][0] <= extent[1] and extent[2] <= t["position"][1] <= extent[3]]
    return {"extent": [extent[0]-80000, extent[1]-80000, extent[2]-147000, extent[3]-147000],
            "captured": raw["finished_utc"], "player": [raw["player_end"][0]-80000, raw["player_end"][1]-147000],
            "traders": traders, "obstacles": obstacles, "floors": floors, "unknown": unknown,
            "section": {"offset": 45, "lower_floor": -3495, "upper_floor": -3410, "ramp_x": [83095, 83481]},
            "counts": {"traders": len(traders), "by_kind": dict(Counter(t[4] for t in traders)),
                       "components": len(raw["instances"]), "validated_instances": sum(not i["geometry_error"] for i in raw["instances"]),
                       "assets": len(raw["assets"]), "obstacle_sections": len(obstacles)}}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("input", type=Path)
    p.add_argument("output", type=Path)
    args = p.parse_args()
    result = build(json.loads(args.input.read_text(encoding="utf-8")))
    args.output.write_text(json.dumps(result, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(json.dumps({**result["counts"], "bytes": args.output.stat().st_size, "floors": len(result["floors"])}))


if __name__ == "__main__":
    main()
