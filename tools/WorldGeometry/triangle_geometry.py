"""Pure geometry operations for the read-only collision research."""
from collections import Counter, defaultdict


def triangle_multiset(vertices, triangles):
    # Ignore vertex deduplication, face order, and winding, retaining multiplicity.
    return Counter(tuple(sorted(tuple(vertices[i]) for i in face)) for face in triangles)


def transform(vertices, position, quaternion, scale):
    x, y, z, w = quaternion
    result = []
    for original in vertices:
        vx, vy, vz = [original[i] * scale[i] for i in range(3)]
        tx, ty, tz = 2 * (y * vz - z * vy), 2 * (z * vx - x * vz), 2 * (x * vy - y * vx)
        result.append([vx + w * tx + y * tz - z * ty + position[0],
                       vy + w * ty + z * tx - x * tz + position[1],
                       vz + w * tz + x * ty - y * tx + position[2]])
    return result


def components(vertices, triangles):
    parents = list(range(len(vertices)))

    def find(i):
        while parents[i] != i:
            parents[i] = parents[parents[i]]
            i = parents[i]
        return i

    def union(a, b):
        parents[find(a)] = find(b)

    equal = {}
    for i, vertex in enumerate(vertices):
        key = tuple(vertex)
        if key in equal:
            union(i, equal[key])
        else:
            equal[key] = i
    for a, b, c in triangles:
        union(a, b)
        union(b, c)
    groups = defaultdict(list)
    for i, face in enumerate(triangles):
        groups[find(face[0])].append(i)
    result = []
    for faces in groups.values():
        ids = {i for face in faces for i in triangles[face]}
        points = [vertices[i] for i in ids]
        result.append({"triangle_indices": faces, "vertex_count": len(ids),
                       "min": [min(v[i] for v in points) for i in range(3)],
                       "max": [max(v[i] for v in points) for i in range(3)]})
    return sorted(result, key=lambda c: c["min"])
