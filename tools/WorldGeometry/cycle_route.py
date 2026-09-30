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
from route_preparation import accept_prepared,prepared_navigation
from moving_preparation import prepare_while_moving


def write(path,value):
    temporary=path.with_suffix(path.suffix+'.tmp')
    temporary.write_text(json.dumps(value,ensure_ascii=False),encoding='utf-8')
    temporary.replace(path)


def run(pid,input_file,output):
    config=json.loads(input_file.read_text(encoding='utf-8-sig'))
    with WalkClient(pid) as client:
        result=run_section(client,config,output)
    # Single centre operations retain the cleanup-before-success contract.
    write(output,result)


def run_section(client,config,output):
    keys={trader_key(t['name']):t.get('traderKey',t['name']) for t in config.get('targets',[])}
    data=load_navigation(config['city'])
    output.parent.mkdir(parents=True,exist_ok=True)
    status=output.with_suffix('.progress.json')
    results={'reason':'preparing','shops':[],'failures':[],'attempted':[]}
    route=None;shops=client.shops;started=time.monotonic()
    pause_baseline=getattr(shops,'pause_seconds_spent',0)
    previous_captures=shops.stats['captured_shops'] if shops else 0
    publish('Preparing movement and checking the current client')
    with output.with_suffix('.motion.jsonl').open('w',encoding='utf-8',buffering=1) as log_file:
        def log(row):
            log_file.write(json.dumps({'at':datetime.now(timezone.utc).isoformat(),**row})+'\n')
        def progress(value):
            write(status,{**value,'mode':config['mode'],'elapsed':round(time.monotonic()-started,1)})
        try:
            if 'readerStartupSeconds' in config:
                log({'type':'reader_startup','seconds':config['readerStartupSeconds']})
            if client.cancelled(): results['reason']='cancelled';return results
            if shops and config['mode']=='prices' and hasattr(shops,'wait_after_capture'):
                pause_result,_=shops.wait_after_capture(client,log,progress)
                if pause_result!='ready':
                    results['reason']=pause_result;return results
            if not client.owned:
                phase=time.monotonic();publish('Checking the client navigation capsule')
                client.wait_navigation_capsule();client.install()
                log({'type':'native_install','seconds':round(time.monotonic()-phase,3)})
            client.navigation_rules=data.get('navigationRules',{})
            guard=getattr(client,'route_guard',None)
            if guard is None:
                guard=WalkGuard(client,data);client.route_guard=guard
            # A price pass can end inside a floor area hidden by the same broad
            # 3D projection. The return route must use the matching map.
            route_data=price_navigation_data(data)
            phase=time.monotonic();publish('Loading surveyed route geometry')
            cached_navigation=prepared_navigation(config,data,client.execution_clearance) if config['mode']=='prices' else None
            cached_navigation=cached_navigation or getattr(client,'route_navigation',None)
            prepared=accept_prepared(config,data,client.position()[:2],client.execution_clearance,cached_navigation) if config['mode']=='prices' else None
            execution=cached_navigation[1].fork() if cached_navigation else Navigation(route_data,clearance=client.execution_clearance)
            # The route planner uses 55 units. Escaping only the narrower
            # execution margin can leave the starting point invalid for A*.
            planning=cached_navigation[0].fork() if cached_navigation else Navigation(route_data)
            log({'type':'navigation_geometry','seconds':round(time.monotonic()-phase,3),
                 'prepared':cached_navigation is not None})
            if config['mode']=='prices':
                client.route_navigation=(planning.fork(),execution.fork())
            client.section_handoff=bool(config.get('continuousSession'))
            if not planning.clear(client.position()[:2],client.position()[:2]):
                if not escape(client,planning,guard,log,time.monotonic()+20):
                    raise RuntimeError('Could not leave the current obstacle clearance')
            start=client.position()[:2]
            publish('Planning a reachable route around obstacles')
            if config['mode']=='center':
                route=center_route(route_data,start,config['center'],config.get('previousDestination'),
                    standing_radius=config.get('centerRadius',200))
            else:
                zone=data.get('collectionZone')
                ignored=[trader_key(t['name']) for t in config['targets'] if not in_collection_zone(zone,t['x'],t['y'])]
                results['ignoredOutsideZone']=ignored
                targets=[{**t,'key':trader_key(t['name'])} for t in config['targets'] if in_collection_zone(zone,t['x'],t['y'])]
                if not targets and not config.get('radarFile'):
                    results['reason']='completed';return results
                route=prepared[0] if prepared else {'anchors':[],'blockers':[],'deferred':[],'points':[]}
                new_reader=shops is None
                if new_reader:
                    shops=WalkShops(client,Path(config.get('shopPrefix',str(output))),95,config.get('radarFile'),
                                    {trader_key(k) for k in config.get('brokerKeys',[])},zone)
                with shops.lock:
                    shops.active.clear()
                    pause=int(config.get('traderPauseSeconds',30))
                    if not 0<=pause<=3600:
                        raise ValueError('Invalid trader pause')
                    shops.trader_pause_seconds=pause
                    shops.radar_file=Path(config['radarFile']) if config.get('radarFile') else None
                    shops.candidate=None
                    shops.allowed_keys={t['key'] for t in targets}
                    shops.allowed_object_ids={t['object_id'] for t in targets}
                    shops.anchor_positions=[(t['x'],t['y']) for t in route['anchors']]
                    shops.coverage_anchor_keys={t['key'] for t in route['anchors']}
                    shops.priority_keys={t['key'] for t in route['anchors']}
                    shops.tracked_targets=targets
                    shops.local_section_mode=bool(config.get('localSection',False))
                    shops.local_section_keys={t['key'] for t in targets}
                    # Bounded absence ends only the previous attempt. A later
                    # host-assigned retry needs a fresh native approach again.
                    shops.unavailable_keys.difference_update(shops.local_section_keys)
                if new_reader:
                    phase=time.monotonic();publish('Preparing nearby shop reader')
                    shops.prepare();shops.start()
                    log({'type':'shop_reader_setup','seconds':round(time.monotonic()-phase,3)})
                log({'type':'reader_session','reused':not new_reader,'pid':client.pid})
                if not targets:
                    # Bootstrap the same reader; pooled packet coordinates only
                    # nominate an approach, never authorize a shop request.
                    shops.active.set()
                    if not shops.discovery_ready.wait(2):
                        if client.cancelled(): results['reason']='cancelled';return results
                        raise RuntimeError(shops.error or 'Radar pool native scan did not finish')
                if not prepared:
                    moving=prepare_while_moving(client,config,data,route_data,targets,(planning,execution),guard,log,progress)
                    if isinstance(moving,dict):
                        results.update(moving);return results
                    if moving:
                        prepared=moving;route=prepared[0]
                    else:route=price_route(route_data,client.position()[:2],targets,navigation=planning)
                with shops.lock:
                    shops.anchor_positions=[(t['x'],t['y']) for t in route['anchors']]
                    shops.coverage_anchor_keys={t['key'] for t in route['anchors']}
                    shops.priority_keys={t['key'] for t in route['anchors']}
                moving=route.get('movingPreparation') or {}
                log({'type':'section_preparation','background_plan':prepared is not None,
                     'background_geometry':cached_navigation is not None,
                     'overlapped':bool(moving),'movingSeconds':moving.get('movementSeconds',0),
                     'seconds':round(time.monotonic()-started-moving.get('movementSeconds',0),3),'targets':len(targets)})
                if route['blockers']:
                    execution.add_blocker(unary_union([
                        Point(anchor['x'],anchor['y']).buffer(45) for anchor in route['blockers']]))
            results['failures']=route['deferred']
            write(output.with_suffix('.plan.json'),route)
            if not route['points']:
                results['reason']='no_route'
                if shops and not config['targets']: results['reason']='completed'
            if route['points']:
                results.update(follow_with_recovery(client,execution,route['points'],log,
                               config.get('duration',240),progress,guard,
                               route_data if config['mode']=='prices' else None))
            if config['mode']=='prices' and results['reason'] not in ('cancelled','keyboard_interrupt',
                    'target_changed_externally','position_jump','shop_error'):
                retry=revisit_missed(client,shops,route,route_data,execution,guard,log,
                                     progress,started,config.get('duration',240),pause_baseline)
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
            if shops and hasattr(shops,'wait_after_capture') and config['mode']=='prices' and results['reason'] not in (
                    'cancelled','keyboard_interrupt','target_changed_externally','position_jump','shop_error'):
                pause_result,_=shops.wait_after_capture(client,log,progress)
                if pause_result!='ready': results['reason']=pause_result
            results['destination']=route.get('destination')
            if config['mode']=='center' and results['reason']=='completed':
                if math.dist(client.position()[:2],config['center'])>config.get('centerRadius',200):
                    results['reason']='outside_center'
                    raise RuntimeError('Stopped outside the configured center zone')
        finally:
            # Session owner performs final rest/cleanup after the finite pass.
            if shops:
                results['radarReviewed']=list(set(results.get('radarReviewed',[]))|
                    shops.captured_keys|shops.unavailable_keys|shops.ignored_outside_keys)
                results['ignoredOutsideZone']=list(set(results.get('ignoredOutsideZone',[]))|shops.ignored_outside_keys)
                results['temporarilyUnavailable']=list(shops.unavailable_keys)
                results['shopStats']=shops.summary()
                if shops.path.exists():
                    # A persistent reader may be appending a large event.
                    # Only newline-terminated records belong to this acknowledgement.
                    text=shops.path.read_bytes()
                    complete=text[:text.rfind(b'\n')+1].decode('utf-8')
                    rows=[json.loads(line) for line in complete.splitlines()]
                    results['shops']=[r for r in rows if r['type']=='shop'][previous_captures:]
                    results['attempted']=list({r['trader_key'] for r in rows if r['type']=='request'})
                captured=shops.captured_keys
                results['failures']=[f for f in results['failures'] if f['key'] not in captured]
                if results['reason']=='completed':
                    attempted=set(results['attempted'])
                    results['failures'].extend({'key':t['key'],'reason':'Passing window missed before shop request'}
                        for t in (route or {}).get('anchors',[]) if t['key'] not in attempted)
            results['finishedAt']=datetime.now(timezone.utc).isoformat()
            # Transport casefold keys are not durable identity. Round-trip the
            # profile's original canonical key (including Unicode nicknames).
            for row in results['shops']: row['trader_key']=keys.get(row['trader_key'],row['trader_key'])
            results['attempted']=[keys.get(k,k) for k in results['attempted']]
            results['temporarilyUnavailable']=[keys.get(k,k) for k in results.get('temporarilyUnavailable',[])]
            for row in results['failures']: row['key']=keys.get(row['key'],row['key'])
            if not config.get('continuousSession'):write(output,results)
    return results


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--pid',type=int,required=True)
    p.add_argument('--input',type=Path,required=True);p.add_argument('--output',type=Path,required=True)
    a=p.parse_args();run(a.pid,a.input,a.output)
