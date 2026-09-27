"""Packaged one-owner route operation; results survive cancellation and retries."""
import argparse
from datetime import datetime,timezone
import json
import math
import time
from pathlib import Path
from shapely.geometry import Point
from shapely.ops import unary_union
from walk_client import WalkClient
from walk_geometry import Navigation
from walk_guard import WalkGuard
from walk_escape import escape
from walk_shops import WalkShops
from walk_recovery import follow_with_recovery
from cycle_revisit import revisit_missed
from walk_shop_policy import trader_key
from cycle_plan import center_route,price_route,price_navigation_data
from worker_progress import publish
from city_maps import load_navigation
from collection_zone import in_collection_zone


def write(path,value):
    temporary=path.with_suffix(path.suffix+'.tmp')
    temporary.write_text(json.dumps(value,ensure_ascii=False),encoding='utf-8')
    temporary.replace(path)


def run(pid,input_file,output):
    config=json.loads(input_file.read_text(encoding='utf-8-sig'))
    keys={trader_key(t['name']):t.get('traderKey',t['name']) for t in config.get('targets',[])}
    data=load_navigation(config['city'])
    output.parent.mkdir(parents=True,exist_ok=True)
    status=output.with_suffix('.progress.json')
    results={'reason':'preparing','shops':[],'failures':[],'attempted':[]}
    route=None;shops=None;started=time.monotonic()
    publish('Preparing movement and checking the current client')
    with WalkClient(pid) as client,output.with_suffix('.motion.jsonl').open('w',encoding='utf-8',buffering=1) as log_file:
        def log(row):
            log_file.write(json.dumps({'at':datetime.now(timezone.utc).isoformat(),**row})+'\n')
        try:
            if client.cancelled(): results['reason']='cancelled';return
            client.wait_navigation_capsule()
            client.navigation_rules=data.get('navigationRules',{})
            client.install();guard=WalkGuard(client,data)
            # A price pass can end inside a floor area hidden by the same broad
            # 3D projection. The return route must use the matching map.
            route_data=price_navigation_data(data)
            execution=Navigation(route_data,clearance=client.execution_clearance)
            # The route planner uses 55 units. Escaping only the narrower
            # execution margin can leave the starting point invalid for A*.
            planning=Navigation(route_data)
            if not planning.clear(client.position()[:2],client.position()[:2]):
                if not escape(client,planning,guard,log,time.monotonic()+20):
                    raise RuntimeError('Could not leave the current obstacle clearance')
            start=client.position()[:2]
            publish('Planning a reachable route around obstacles')
            if config['mode']=='center':
                route=center_route(route_data,start,config['center'],config.get('previousDestination'))
            else:
                zone=data.get('collectionZone')
                ignored=[trader_key(t['name']) for t in config['targets'] if not in_collection_zone(zone,t['x'],t['y'])]
                results['ignoredOutsideZone']=ignored
                targets=[{**t,'key':trader_key(t['name'])} for t in config['targets'] if in_collection_zone(zone,t['x'],t['y'])]
                if not targets and not config.get('radarFile'):
                    results['reason']='completed';return
                route=price_route(route_data,start,targets)
                shops=WalkShops(client,output,95,config.get('radarFile'),
                                {trader_key(k) for k in config.get('brokerKeys',[])},zone)
                shops.allowed_keys={t['key'] for t in targets}
                shops.allowed_object_ids={t['object_id'] for t in targets}
                shops.anchor_positions=[(t['x'],t['y']) for t in route['anchors']]
                shops.priority_keys={t['key'] for t in route['anchors']}
                shops.tracked_targets=targets
                shops.local_section_mode=bool(config.get('localSection',False))
                shops.local_section_keys={t['key'] for t in targets}
                publish('Preparing nearby shop reader')
                shops.prepare();shops.start()
                if not targets:
                    # Bootstrap the same reader; pooled packet coordinates only
                    # nominate an approach, never authorize a shop request.
                    shops.active.set()
                    if not shops.discovery_ready.wait(2):
                        if client.cancelled(): results['reason']='cancelled';return
                        raise RuntimeError(shops.error or 'Radar pool native scan did not finish')
                if route['blockers']:
                    execution.add_blocker(unary_union([
                        Point(anchor['x'],anchor['y']).buffer(45) for anchor in route['blockers']]))
            results['failures']=route['deferred']
            write(output.with_suffix('.plan.json'),route)
            if not route['points']:
                results['reason']='no_route'
                if shops and not config['targets']: results['reason']='completed'
            def progress(value):
                write(status,{**value,'mode':config['mode'],'elapsed':round(time.monotonic()-started,1)})
            if route['points']:
                results.update(follow_with_recovery(client,execution,route['points'],log,
                               config.get('duration',240),progress,guard,
                               route_data if config['mode']=='prices' else None))
            if config['mode']=='prices' and results['reason'] not in ('cancelled','keyboard_interrupt',
                    'target_changed_externally','position_jump','shop_error'):
                retry=revisit_missed(client,shops,route,route_data,execution,guard,log,
                                     progress,started,config.get('duration',240))
                if retry is not None:
                    results['radarReviewed']=[key for key in retry['targets'] if key in shops.dynamic_targets]
                    results['revisit']={'reason':retry['reason'],
                                        'seconds':retry['seconds'],
                                        'recoveries':len(retry['recoveries'])}
                    if retry['reason']=='completed':
                        results['position']=retry['position']
                        results['reason']='completed'
                    elif retry['reason'] in ('cancelled','keyboard_interrupt',
                                             'target_changed_externally','position_jump'):
                        results['reason']=retry['reason']
            results['destination']=route.get('destination')
            if config['mode']=='center' and results['reason']=='completed':
                if math.dist(client.position()[:2],config['center'])>500:
                    results['reason']='outside_center'
                    raise RuntimeError('Stopped outside the configured center zone')
        finally:
            # Close hooks BEFORE publishing success. Preserve already captured
            # shops even when the route was stopped or a later target failed.
            cleanup_error=None
            try:
                client.close()
            except Exception as error:
                cleanup_error=error
                if results['reason']=='preparing':
                    results['reason']='cleanup_failed'
            if shops:
                results['radarReviewed']=list(set(results.get('radarReviewed',[]))|
                    shops.captured_keys|shops.unavailable_keys|shops.ignored_outside_keys)
                results['ignoredOutsideZone']=list(set(results.get('ignoredOutsideZone',[]))|shops.ignored_outside_keys)
                results['temporarilyUnavailable']=list(shops.unavailable_keys)
                results['shopStats']=shops.summary()
                if shops.path.exists():
                    rows=[json.loads(line) for line in shops.path.read_text(encoding='utf-8').splitlines()]
                    results['shops']=[r for r in rows if r['type']=='shop']
                    results['attempted']=list({r['trader_key'] for r in rows if r['type']=='request'})
                captured=shops.captured_keys
                results['failures']=[f for f in results['failures'] if f['key'] not in captured]
                if results['reason']=='completed':
                    attempted=set(results['attempted'])
                    results['failures'].extend({'key':t['key'],'reason':'Passing window missed before shop request'}
                        for t in route['anchors'] if t['key'] not in attempted)
            results['finishedAt']=datetime.now(timezone.utc).isoformat()
            # Transport casefold keys are not durable identity. Round-trip the
            # profile's original canonical key (including Unicode nicknames).
            for row in results['shops']: row['trader_key']=keys.get(row['trader_key'],row['trader_key'])
            results['attempted']=[keys.get(k,k) for k in results['attempted']]
            results['temporarilyUnavailable']=[keys.get(k,k) for k in results.get('temporarilyUnavailable',[])]
            for row in results['failures']: row['key']=keys.get(row['key'],row['key'])
            write(output,results)
            if cleanup_error is not None:
                raise cleanup_error


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--pid',type=int,required=True)
    p.add_argument('--input',type=Path,required=True);p.add_argument('--output',type=Path,required=True)
    a=p.parse_args();run(a.pid,a.input,a.output)
