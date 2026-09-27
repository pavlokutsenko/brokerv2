"""Guarded native geometry query on the owned game-thread command bridge."""
import argparse
import json
import math
from pathlib import Path
import struct

from walk_client import WalkClient, capture, invoke
from inspect_collision import Reflection
from lu4_target_controller import write_exact
from inspect_world import GOBJECTS, read_object


class CapsuleProbe:
    def __init__(self, client):
        self.c = client
        if not client.owned:
            raise RuntimeError("capsule queries require the owned command bridge")
        self.function = client.function(4723, "CapsuleTraceSingleForObjects", "KismetSystemLibrary", 401)
        owner = client.m.u64(self.function + 0x20)
        self.cdo = client.m.u64(owner + 0x110)
        if client.s.describe(self.cdo)["class"] != "KismetSystemLibrary":
            raise RuntimeError("Kismet CDO guard failed")
        ref = Reflection(client.s)
        expected = {"WorldContextObject": (0, 8), "Start": (8, 24), "End": (32, 24),
                    "Radius": (56, 4), "HalfHeight": (60, 4), "ObjectTypes": (64, 16),
                    "bTraceComplex": (80, 1), "ActorsToIgnore": (88, 16),
                    "OutHit": (112, 248), "bIgnoreSelf": (360, 1), "ReturnValue": (400, 1)}
        for name, (offset, size) in expected.items():
            if ref.at(self.function, name, size) != offset:
                raise RuntimeError(f"capsule parameter layout changed: {name}")
        inner = client.m.u64(ref.fields(self.function)["ObjectTypes"]["field"] + 0x78)
        if client.m.i32(inner + 0x34) != 1:
            raise RuntimeError("object-type array is not byte-sized")
        hit_type = ref.struct_type(self.function, "OutHit")
        for name, offset, size in (("Time", 4, 4), ("Distance", 8, 4), ("ImpactPoint", 40, 24),
                                   ("ImpactNormal", 88, 24), ("PenetrationDepth", 160, 4)):
            if ref.at(hit_type, name, size) != offset:
                raise RuntimeError("FHitResult layout changed")
        state = capture.load_state()
        self.extra = int(state["command_params_address"]) + 0x600
        write_exact(client.client, client.pid, self.extra, bytes([0, 1]))
        write_exact(client.client, client.pid, self.extra + 16, struct.pack("<Q", client.world["player_actor"]))

    def trace(self, start, end, radius=11, half_height=21, complex_trace=False):
        if not all(math.isfinite(v) for v in (*start, *end, radius, half_height)) or math.dist(start, end) > 1000:
            raise ValueError("unbounded capsule query")
        params = bytearray(401)
        struct.pack_into("<Q6d2f", params, 0, self.c.world["world"], *start, *end, radius, max(radius, half_height))
        struct.pack_into("<Qii", params, 64, self.extra, 2, 2)
        params[80] = int(complex_trace)
        struct.pack_into("<Qii", params, 88, self.extra + 16, 1, 1)
        params[360] = 1
        result = invoke(self.cdo, self.function, bytes(params))
        return self.decode(result[112:360], result[400])

    def decode(self, result, blocked):
        self.c.m.pages.clear()
        component_index = struct.unpack_from('<i', result, 216)[0]
        component = read_object(self.c.m, self.c.base+GOBJECTS, component_index) if blocked else 0
        owner=self.c.m.u64(component+0x20) if component else 0
        owner_name=self.c.s.name(owner) if owner else None
        if owner_name and (len(owner_name)>160 or any(ord(c)<32 for c in owner_name)):
            owner_name=None
        return {"blocked": bool(blocked), "time": struct.unpack_from("<f", result, 4)[0],
                "distance": struct.unpack_from("<f", result, 8)[0],
                "point": struct.unpack_from("<3d", result, 40),
                "normal": struct.unpack_from("<3d", result, 88),
                "penetration": struct.unpack_from("<f", result, 160)[0], "flags": result[173],
                "component": self.c.s.describe(component) if component else None,
                "outer": owner_name,
                "outer_class":self.c.s.name(self.c.m.u64(owner+0x10)) if owner else None}


class PawnCapsuleProbe(CapsuleProbe):
    def __init__(self, client):
        super().__init__(client)
        self.function = client.function(4722, 'CapsuleTraceSingleByProfile', 'KismetSystemLibrary', 393)
        ref = Reflection(client.s)
        for name, offset, size in (('ProfileName',64,8),('ActorsToIgnore',80,16),('OutHit',104,248),('ReturnValue',392,1)):
            if ref.at(self.function,name,size)!=offset:
                raise RuntimeError('profile query layout changed')
        cls=client.m.u64(client.world['player_actor']+0x10)
        for _ in range(16):
            if client.s.name(cls)=='Pawn':
                self.profile=client.m.read(cls+0x18,8);break
            cls=client.m.u64(cls+0x40)
        else:
            raise RuntimeError('Pawn FName unavailable')
        self.ignore_buffer, _ = client.client.allocate_process_memory(client.pid,0x10000)
        client.cleanup_callbacks.append(lambda:client.release_command_buffer(self.ignore_buffer))

    def refresh_ignore(self):
        # Current actor array each query; never pass stale cached actor pointers.
        self.c.m.pages.clear()
        actors=self.c.s.array(self.c.world['persistent_level'],0xA0,65536)
        ignored=[self.c.world['player_actor']]
        for actor in actors:
            if actor and self.c.s.name(self.c.m.u64(actor+0x10))=='CharacterPlayer_C':
                ignored.append(actor)
        if len(ignored)>8192:
            raise RuntimeError('ignore actor budget exceeded')
        write_exact(self.c.client,self.c.pid,self.ignore_buffer,struct.pack(f'<{len(ignored)}Q',*ignored))
        return len(ignored)

    def trace(self,start,end,radius=11,half_height=21,complex_trace=False):
        if not all(math.isfinite(v) for v in (*start,*end)) or math.dist(start,end)>1000:
            raise ValueError('unbounded capsule query')
        params=bytearray(393)
        struct.pack_into('<Q6d2f',params,0,self.c.world['world'],*start,*end,radius,max(radius,half_height))
        params[64:72]=self.profile;params[72]=int(complex_trace)
        count=self.refresh_ignore()
        struct.pack_into('<Qii',params,80,self.ignore_buffer,count,count)
        params[352]=1
        result=invoke(self.cdo,self.function,bytes(params))
        return self.decode(result[104:352],result[392])


if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("pid", type=int)
    p.add_argument("--json", type=Path, required=True)
    p.add_argument('--profile', action='store_true')
    a = p.parse_args()
    controls = [("clear_plaza", (82300, 148180, -3470), (82450, 148180, -3470)),
                ("monument", (81650, 148610, -3450), (82200, 148610, -3450)),
                ("temple_front", (83700, 148780, -3406), (83800, 148780, -3406)),
                ("temple_escape", (83708, 148789, -3406), (83570, 148789, -3406))]
    with WalkClient(a.pid) as client:
        source = client.position()
        client.install()
        probe = PawnCapsuleProbe(client) if a.profile else CapsuleProbe(client)
        results = [{"control": name, "start": start, "end": end, **probe.trace(start, end)} for name, start, end in controls]
        report = {"source": source, "after": client.position(), "controls": results}
    a.json.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))
