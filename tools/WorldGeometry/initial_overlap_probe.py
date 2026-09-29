"""One bounded ordinary departure after a freshly verified initial NPC contact."""
import argparse
import json
import math
import time
from datetime import datetime,timezone
from pathlib import Path
from city_maps import load_navigation
from cycle_plan import price_navigation_data
from walk_client import WalkClient
from walk_geometry import Navigation
from walk_guard import WalkGuard


def run(pid,output):
    result={'pid':pid,'at':datetime.now(timezone.utc).isoformat(),'moved':False}
    try:
        with WalkClient(pid) as client:
            client.wait_navigation_capsule();client.install()
            data=load_navigation('Giran');guard=WalkGuard(client,data)
            nav=Navigation(price_navigation_data(data),clearance=client.execution_clearance)
            source=client.position();result['source']=source
            guard.clear_level_escape(source,(source[0]+35,source[1]))
            hit=guard.last_hit or {};result['initialHit']=hit
            if hit.get('outer_class')!='CharacterNpc_C':
                result['reason']='observed NPC contact no longer present';return
            normal=hit.get('normal') or (0,0,0);length=math.hypot(*normal[:2])
            if length<.8:raise RuntimeError('NPC departure normal not valid')
            goal=(source[0]+65*normal[0]/length,source[1]+65*normal[1]/length)
            result['goal']=goal
            if not nav.clear_forbidden(source[:2],goal):raise RuntimeError('Hard exclusion blocks departure')
            floor=guard.ground.height(*goal)
            if floor is not None and abs(floor-(source[2]-client.capsule_half_height))>28:
                raise RuntimeError('Departure ground height unsafe')
            if not guard.clear_outward_escape(source,goal):
                result['followupHit']=guard.last_hit;raise RuntimeError('Outward native sweep remains blocked')
            result['outwardNativeSweepClear']=True
            client.move(goal);started=time.monotonic();samples=[]
            while time.monotonic()-started<2.2:
                position=client.position();samples.append(position)
                if math.dist(position[:2],goal)<18 or client.cancelled():break
                time.sleep(.08)
            client.stop(force=True);position=client.position()
            result.update(position=position,samples=samples,
                          moved=math.dist(source[:2],position[:2])>=12,
                          displacement=math.dist(source[:2],position[:2]),reason='ordinary guarded departure')
            if not result['moved']:raise RuntimeError('No observed departure movement')
    finally:
        output.write_text(json.dumps(result,indent=2),encoding='utf-8')


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--pid',type=int,required=True);p.add_argument('--output',type=Path,required=True)
    a=p.parse_args();run(a.pid,a.output)
