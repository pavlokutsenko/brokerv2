"""Read-only bounded discovery of controller movement/camera functions."""
import argparse
import json
from pathlib import Path
from inspect_world import BUILD, GWORLD, Lu4MemoryClient, Memory, Survey, pe_info, validate_world_slot, pointer
from inspect_collision import Reflection


def inspect(pid):
    with Lu4MemoryClient() as client:
        base = client.process_base(pid)
        m = Memory(client, pid)
        if tuple(pe_info(m, base)[k] for k in ("timestamp", "image_size")) != BUILD:
            raise RuntimeError("unsupported build")
        s, rows = Survey(m, base), []
        r = Reflection(s)
        w = validate_world_slot(m, base+GWORLD, base, BUILD[1])
        if not w:
            raise RuntimeError("no validated world")
        cls = m.u64(w["controller"]+0x10)
        fields = r.fields(cls)
        properties = {n:v for n,v in fields.items() if any(t in n.lower() for t in ("camera", "rotation", "move", "yaw"))}
        seen = set()
        while pointer(cls) and cls not in seen and len(seen)<16:
            seen.add(cls)
            obj = m.u64(cls+0x48)
            visited = set()
            while pointer(obj) and obj not in visited and len(visited)<4096:
                visited.add(obj)
                d = s.describe(obj)
                if d["class"] == "Function" and any(t in d["name"].lower() for t in ("move", "stop", "camera", "rotation")):
                    rows.append({**d,"owner":s.name(cls),"index":m.i32(obj+0xC),
                                 "params_size":m.unpack("<H",obj+0xB6,0),"fields":r.fields(obj)})
                obj=m.u64(obj+0x28)
            cls=m.u64(cls+0x40)
        return {"pid":pid,"world":w,"properties":properties,"functions":rows}


if __name__ == "__main__":
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("pid",type=int)
    p.add_argument("--json",type=Path,required=True)
    a=p.parse_args()
    result=inspect(a.pid)
    a.json.write_text(json.dumps(result,indent=2),encoding="utf-8")
    print(json.dumps(result,indent=2))
