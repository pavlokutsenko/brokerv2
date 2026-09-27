"""Experimental smooth market walk with optional asynchronous nearby-shop reads."""
import argparse
from datetime import datetime,timezone
import hashlib
import json
import math
import os
import struct
from pathlib import Path
import time

from walk_client import WalkClient
from walk_geometry import Navigation
from walk_plan import plan,nearby_segments
from walk_follow import Path as RoutePath,follow
from walk_guard import WalkGuard
from walk_recovery import follow_with_recovery
from walk_escape import escape
from city_maps import load_navigation
from walk_course import course
from bisect import bisect_right
import numpy as np

ROOT=Path(__file__).resolve().parents[2]


def run(args):
    if args.single_targets:
        if args.obstacle_course or args.resume_result:
            raise ValueError('single-target test uses its own fresh route')
        if args.shop_radius<75:
            raise ValueError('single-target fly-bys need shop radius >=75')
        args.read_shops=True
    raw=args.map.read_bytes() if args.map else json.dumps(load_navigation(args.city)).encode('utf-8')
    data=json.loads(raw)
    data={**data,'navigationRules':load_navigation(args.city).get('navigationRules',{})}
    folder=Path(os.environ["LOCALAPPDATA"])/"PriceCheckCollector/research/market-walk"/str(args.pid)
    folder.mkdir(parents=True,exist_ok=True)
    stamp=datetime.now().strftime("%Y%m%d-%H%M%S")
    prefix=folder/stamp
    if args.stop:
        (folder/"STOP").touch()
        print("Stop requested")
        return 0
    with WalkClient(args.pid) as client:
        client.navigation_rules=data.get('navigationRules',{})
        shops=None
        if args.read_shops:
            from walk_shops import WalkShops
            shops=WalkShops(client,prefix,args.shop_radius)
            if args.run:
                shops.prepare()
        source=client.position()
        guard=None
        execution=Navigation(data,clearance=client.execution_clearance)
        if args.run and not execution.clear(source[:2],source[:2]):
            print('Current position is inside planning clearance. Checking a short departure; F8 stops.',flush=True)
            client.install();guard=WalkGuard(client,data)
            with prefix.with_suffix('.preflight.jsonl').open('w',encoding='utf-8',buffering=1) as out:
                if not escape(client,execution,guard,lambda v:out.write(json.dumps(v)+'\n'),time.monotonic()+15):
                    raise RuntimeError('cannot leave obstacle clearance using verified short movement')
            source=client.position()
        map_hash=hashlib.sha256(raw).hexdigest()
        if args.resume_result:
            previous=json.loads(args.resume_result.read_text(encoding='utf-8'))
            if previous.get('recoveries'):
                raise RuntimeError('replanned run cannot be resumed against its original arc; create a fresh plan')
            old=json.loads(Path(previous['plan']).read_text(encoding='utf-8'))
            if old.get('map_sha256')!=map_hash:
                raise RuntimeError('resume map differs from original plan')
            old_path=RoutePath(old['points'])
            progress=float(previous['progress'])
            index=min(len(old['points'])-1,bisect_right(old_path.distance,progress+25))
            nav=Navigation(data,clearance=client.execution_clearance)
            prefix_path=nav.shortest(source[:2],old['points'][index])
            remaining=[*prefix_path,*old['points'][index+1:]]
            if not all(nav.clear(tuple(a),tuple(b)) for a,b in zip(remaining,remaining[1:])):
                raise RuntimeError('remaining route fails execution clearance')
            traders=np.array([[t[1]+80000,t[2]+147000] for t in data['traders']])
            route={**old,'points':remaining,'start':source[:2],'visits':None,
                   'length':sum(math.dist(a,b) for a,b in zip(remaining,remaining[1:])),
                   'planned_nearby':int(nearby_segments(traders,remaining,old['radius']).sum()),
                   'resumed_from':str(args.resume_result)}
        else:
            planner=course if args.obstacle_course else plan
            if args.single_targets:
                from passby_event_collector import current_traders
                from walk_single_targets import plan_singles
                snapshot={'pid':client.pid,**{k:hex(client.world[k]) for k in ('persistent_level','player_actor')}}
                live,_=current_traders(client.client,snapshot)
                planner=lambda data,start:plan_singles(data,start,live)
            planning=Navigation(data)
            if planning.clear(source[:2],source[:2]):
                route=planner(data,source[:2])
            else:
                execution=Navigation(data,clearance=client.execution_clearance)
                starts=sorted(planning.nodes.values(),key=lambda p:math.dist(source[:2],p))
                start=next((p for p in starts if math.dist(source[:2],p)<=240 and execution.clear(source[:2],p)),None)
                if start is None:
                    raise RuntimeError('no clear connection from current position to planning margin')
                route=planner(data,start)
                route['points']=[source[:2],*route['points'],source[:2]]
                route['length']+=2*math.dist(source[:2],start)
                route['start']=source[:2]
        route["map_sha256"]=map_hash
        route['capsule_radius']=client.capsule_radius
        route['execution_clearance']=client.execution_clearance
        plan_file=prefix.with_suffix(".plan.json")
        plan_file.write_text(json.dumps(route,indent=2),encoding="utf-8")
        if route.get('targets'):
            print(json.dumps({'single_targets':[{'name':t['name'],'corner':t['corner'],
                  'planned_distance':round(t['planned_min_distance'],1)} for t in route['targets']]},ensure_ascii=False),flush=True)
            if shops:
                shops.allowed_keys={t['trader_key'] for t in route['targets']}
        print(json.dumps({"plan":str(plan_file),"length":round(route['length']),"planned_nearby":route['planned_nearby'],
                          "traders":route['traders'],"radius":route['radius'],"coverage_anchors":route['visits']},ensure_ascii=False),flush=True)
        if not args.run:
            return 0
        nav=Navigation(data,clearance=client.execution_clearance)
        if route.get('targets'):
            from walk_single_targets import add_exclusions
            add_exclusions(nav,route['targets'])
        print("F8 or Ctrl+C to stop. Starting in 5 seconds; "+
              ("nearby shop reads enabled." if shops else "no shops or targets."),flush=True)
        for _ in range(50):
            if client.cancelled():
                print("Cancelled before bridge install",flush=True);return 0
            time.sleep(.1)
        if math.dist(client.position()[:2],source[:2])>15:
            raise RuntimeError("player moved while planning; rerun from the new position")
        if not client.owned:
            client.install()
        guard=guard or WalkGuard(client,data)
        if shops:
            shops.start()
        samples=[]
        with prefix.with_suffix(".jsonl").open("w",encoding="utf-8",buffering=1) as output:
            def log(value):
                value.setdefault('at',datetime.now(timezone.utc).isoformat())
                output.write(json.dumps(value,ensure_ascii=False)+"\n")
                if value['type']=='sample':
                    samples.append(value['position'][:2])
            log({"type":"start","pid":args.pid,"time":datetime.now(timezone.utc).isoformat(),
                 "source":source,"camera_direction":client.camera_direction(),"map_sha256":route['map_sha256']})
            try:
                result=follow_with_recovery(client,nav,route["points"],log,args.duration,
                              lambda v:print(json.dumps(v),flush=True),guard)
            except KeyboardInterrupt:
                result={"reason":"keyboard_interrupt"}
            except Exception as error:
                result={"reason":"error","error":str(error)}
            if result['reason'] in ('error','keyboard_interrupt'):
                try:
                    client.stop()
                except Exception as stop_error:
                    result['stop_error']=str(stop_error)
            if shops:
                shops.close()
                result['shops']=shops.summary()
                if shops.error:
                    result['reason']='shop_error'
            try:
                result['position']=client.position()
                result["camera_end"]=client.camera_direction()
                selected=struct.unpack('<Q',client.m.read(client.world['controller']+0x898,8))[0]
                result['selected_actor']=hex(selected)
                result["selected_unchanged"]=selected==client.initial_selected
            except Exception as read_error:
                result['final_read_error']=str(read_error)
            traders=np.array([[t[1]+80000,t[2]+147000] for t in data["traders"]])
            result["observed_nearby"]=int(nearby_segments(traders,samples,route['radius']).sum())
            result["captured_traders"]=len(traders)
            result["coverage_radius"]=route['radius']
            result["log"]=str(prefix.with_suffix('.jsonl'))
            result["plan"]=str(plan_file)
            if shops and route.get('targets'):
                from walk_single_targets import target_report
                result['single_targets']=target_report(route,prefix.with_suffix('.jsonl'),shops.path)
            if route.get('landmarks'):
                result['landmarks']=[{'name':o['name'],'corners_passed':sum(
                    any(math.dist(p,corner)<=100 for p in samples) for corner in o['corners'])}
                    for o in route['landmarks']]
            prefix.with_suffix(".result.json").write_text(json.dumps(result,indent=2),encoding="utf-8")
            print(json.dumps({**result,'recoveries':len(result.get('recoveries',[])),
                              'segments':len(result.get('segments',[]))},ensure_ascii=False,indent=2),flush=True)
    return 0 if result['reason'] in ('completed','cancelled','keyboard_interrupt','duration_limit') else 2


if __name__ == "__main__":
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--pid",type=int,required=True)
    p.add_argument("--city",default='Giran')
    p.add_argument("--map",type=Path)
    mode=p.add_mutually_exclusive_group()
    mode.add_argument("--run",action="store_true")
    mode.add_argument("--stop",action="store_true")
    p.add_argument("--duration",type=float,default=600)
    p.add_argument("--resume-result",type=Path)
    p.add_argument('--obstacle-course',action='store_true')
    p.add_argument('--read-shops',action='store_true')
    p.add_argument('--shop-radius',type=float,default=85)
    p.add_argument('--single-targets',action='store_true')
    raise SystemExit(run(p.parse_args()))
