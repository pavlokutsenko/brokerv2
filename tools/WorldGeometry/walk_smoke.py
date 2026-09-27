"""Bounded movement/stop control: at most one 120-unit world-coordinate order."""
import argparse
import json
import math
from pathlib import Path
import time
from walk_client import WalkClient


def smoke(pid):
    with WalkClient(pid) as c:
        c.install()
        source=c.position()
        camera=c.camera_direction()
        destination=[source[0]+120,source[1]]
        c.move(destination)
        samples=[]
        started=time.monotonic()
        while time.monotonic()-started<.35 and not c.cancelled():
            samples.append([time.monotonic()-started,*c.position()])
            time.sleep(.04)
        before=c.position()
        c.stop()
        time.sleep(.8)
        stopped=c.position()
        time.sleep(.6)
        settled=c.position()
        result={"source":source,"camera_direction":camera,"destination":destination,
                "before_stop":before,"after_stop":stopped,"settled":settled,"samples":samples,
                "movement":math.dist(source[:2],before[:2]),"stop_drift":math.dist(stopped[:2],settled[:2]),
                "selected_unchanged":c.m.u64(c.world["controller"]+0x898)==c.initial_selected}
        if result["movement"]<5 or result["stop_drift"]>3:
            result["passed"]=False
        else:
            result["passed"]=True
        return result


if __name__ == "__main__":
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("pid",type=int)
    p.add_argument("--json",type=Path,required=True)
    a=p.parse_args()
    result=smoke(a.pid)
    a.json.write_text(json.dumps(result,indent=2),encoding="utf-8")
    print(json.dumps(result,indent=2))
