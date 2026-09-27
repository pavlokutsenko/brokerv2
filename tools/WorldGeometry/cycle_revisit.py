"""Stop, replan approaches and require an exact reply before success."""
import math
import time

from cycle_recheck_plan import recheck_route
from walk_follow import follow
from walk_recovery import follow_with_recovery
from walk_geometry import Navigation
from walk_escape import escape
from collection_zone import in_collection_zone
from walk_shop_policy import RADAR_DISCOVERY_RADIUS
from radar_pass_plan import radar_pass_route

STOP_REASONS={'cancelled','keyboard_interrupt','target_changed_externally','position_jump','shop_error'}


def unavailable_after_approach(shops,target,position,now):
    """Skip this pass only after fresh native absence at an actual read point."""
    key=target['key']
    if target.get('rebind') and target['object_id']==0 and (
            getattr(shops,'unbound_absence_scans',{}).get(key,0)<3
            or not 0<=now-getattr(shops,'unbound_scan_at',0)<=1):
        return False  # An unbound scan cannot prove that the shop has left.
    return (not shops.error and key not in shops.live_targets
        and key not in shops.requested_keys and key not in shops.captured_keys
        and getattr(shops,'absence_scans',{}).get(key,0)>=3
        and 0<=now-getattr(shops,'last_actor_scan_at',0)<=1
        and math.dist(position[:2],(target['x'],target['y']))<=100)


def select_revisit_targets(anchors, captured, seen_live, position, limit=None):
    unique={t['key']:t for t in anchors if t['key'] in seen_live and t['key'] not in captured}
    result=sorted(unique.values(),key=lambda t:math.dist(position,(t['x'],t['y'])))
    return result if limit is None else result[:limit]


def initial_revisit_targets(shops,route,position):
    known=[*shops.tracked_targets,*shops.dynamic_targets.values(),*shops.dynamic_seen_live.values()]
    # Every assigned broker shop needs an approach, even if the main path did
    # not stream its actor in. Radar-only visits still require native admission.
    eligible=({t['key'] for t in shops.tracked_targets}
              |{t['key'] for t in route['deferred']}|{t['key'] for t in route['anchors']}
              |shops.trace_actor_present|shops.requested_keys|set(shops.dynamic_seen_live))
    if getattr(shops,'local_section_mode',False):
        # The local database already ordered the whole pass. A nearby future
        # section may be read on the fly, but must not hijack this section's path.
        eligible &= shops.local_section_keys
    return select_revisit_targets(known,shops.captured_keys,eligible,position)


def add_live_radar_targets(pending, shops, attempted, position, now):
    """New shops remain eligible while individual approaches are in progress."""
    added=[]
    for key,target in list(shops.dynamic_seen_live.items()):
        if getattr(shops,'local_section_mode',False) and key not in shops.local_section_keys:
            continue
        if key in getattr(shops,'reopened_keys',set()):
            attempted.discard(key);shops.reopened_keys.discard(key)
        # The reader admits a matching native actor within the discovery radius once. A long
        # current approach must not discard that visit merely because it ends
        # farther away or more than2seconds later. Requests still validate the
        # live actor/range; handled and the route budget bound retries.
        admitted=target.get('admitted_at') is not None or target.get('continuation_nomination',False)
        if (key in pending or key in attempted or key in shops.captured_keys
                or (not admitted and (now-target['seen_at']>2
                    or math.dist(position[:2],(target['x'],target['y']))>RADAR_DISCOVERY_RADIUS))
                or not in_collection_zone(shops.collection_zone,target['x'],target['y'])):
            continue
        pending[key]=target;added.append(key)
    return added


def add_late_broker_misses(pending,shops,handled):
    """Include shops first encountered while approaching another target."""
    seen=shops.trace_actor_present|shops.requested_keys
    added=[]
    for target in shops.tracked_targets:
        key=target['key']
        if (key not in seen or key in pending or key in handled
                or key in shops.captured_keys or key in shops.unavailable_keys):
            continue
        pending[key]=target;added.append(key)
    return added


def hold_read(client,shops,target,log,deadline):
    """Use the existing reader; zero-distance moves refresh ordinary server origins."""
    key=target['key'];shops.priority_keys={key}
    shops.read_hold_key=key
    requests=shops.stats['requests'];timeouts=shops.stats['timeouts']
    with shops.lock:
        for token in list(shops.retry):
            if token[0]==key: shops.retry[token]=0
    shops.active.set();last_move=0
    try:
        while time.monotonic()<deadline:
            if client.cancelled(): return 'cancelled'
            if shops.error: return 'shop_error'
            if key in shops.captured_keys: return 'captured'
            if key in shops.unavailable_keys: return 'temporarily_unavailable'
            client.position()  # refresh controller pages before target validation
            selected=client.m.u64(client.world['controller']+0x898)
            if selected!=client.initial_selected and not shops.allowed_target(selected):
                return 'target_changed_externally'
            now=time.monotonic()
            if unavailable_after_approach(shops,target,client.position(),now):
                shops.unavailable_keys.add(key)
                return 'temporarily_unavailable'
            if now-last_move>=.35:
                client.move(client.position()[:2]);last_move=time.monotonic()
            time.sleep(.04)
        return 'no_exact_reply'
    finally:
        shops.read_hold_key=None
        if key not in shops.captured_keys:
            shops.active.clear();client.stop()
        live=shops.live_targets.get(key)
        position=client.position()
        log({'type':'recheck_read','key':key,'captured':key in shops.captured_keys,
             'actor_present':live is not None,'position':position,
             'distance':math.dist(position[:2],(live['x'],live['y'])) if live else None,
             'vertical_gap':abs(position[2]-live['z']) if live else None,
             'server_age':time.monotonic()-shops.server_at,
             'absence_scans':shops.absence_scans.get(key,0),
             'unavailable_for_pass':key in shops.unavailable_keys,
             'requests':shops.stats['requests']-requests,'timeouts':shops.stats['timeouts']-timeouts})


def revisit_missed(client, shops, route, route_data, execution, guard, log,
                   progress, started, duration):
    # Broker-time packet observations can be temporarily outside the native
    # actor window. Approach their observed location once; the ordinary reader
    # still requires the matching live actor and <=95 before every request.
    targets=initial_revisit_targets(shops,route,client.position()[:2])
    pending={t['key']:t for t in targets}
    if not pending and not shops.dynamic_seen_live: return None
    begun=time.monotonic();results=[];attempted=[];unresolved=[]
    old_dynamic=shops.dynamic_detours_enabled
    shops.dynamic_detours_enabled=False
    try:
        # Do not reconstruct the full city polygons, ground cliffs and grid
        # twice for every shop. Each execution gets an isolated cheap fork.
        prepared=time.monotonic()
        departure=Navigation(route_data,clearance=client.execution_clearance)
        log({'type':'recheck_navigation_ready','seconds':round(time.monotonic()-prepared,3)})
        handled=set();handoff=False
        while True:
            current=client.position()[:2]
            added=add_live_radar_targets(pending,shops,handled,current,time.monotonic())
            if added: log({'type':'recheck_radar_added','keys':added})
            late=add_late_broker_misses(pending,shops,handled)
            if late: log({'type':'recheck_late_broker_added','keys':late})
            pending={key:t for key,t in pending.items() if key not in shops.captured_keys}
            if not pending: break
            if client.cancelled(): break
            pending={key:t for key,t in pending.items() if key not in shops.unavailable_keys}
            if not pending: break
            if time.monotonic()-started>=duration-8:
                unresolved.extend(pending)
                break
            target=min(pending.values(),key=lambda t:math.dist(current,(t['x'],t['y'])))
            pending.pop(target['key']);handled.add(target['key'])
            approach_started=time.monotonic()
            if not handoff: client.stop()
            handoff=False
            target=shops.live_targets.get(target['key'],target)
            if not in_collection_zone(route_data.get('collectionZone'),target['x'],target['y']):
                shops.ignored_outside_keys.add(target['key'])
                log({'type':'target_outside_collection_zone','key':target['key']})
                continue
            attempted.append(target['key'])
            progress({'detail':f"Approach {len(attempted)} · {len(pending)} remaining: {target['name']}",
                      'shops':shops.stats['captured_shops'],'exact':shops.stats['exact_shops']})
            if not departure.clear(client.position()[:2],client.position()[:2]):
                escape(client,departure,guard,log,min(started+duration-8,time.monotonic()+10))
            planned=time.monotonic()
            onward_target=min(pending.values(),key=lambda t:math.dist(current,(t['x'],t['y']))) if pending else None
            passing=radar_pass_route(route_data,client.position()[:2],target,
                client.execution_clearance,navigation=departure,
                onward=(onward_target['x'],onward_target['y']) if onward_target else None)
            if passing: points,nav=passing;direct=False
            else: points,nav,direct=recheck_route(route_data,client.position()[:2],target,
                                                 client.execution_clearance,navigation=departure)
            plan_seconds=time.monotonic()-planned
            log({'type':'recheck_plan','key':target['key'],'direct_probe':direct,'points':points,
                 'plan_seconds':round(plan_seconds,4),'passing_arc':bool(passing),
                 'passing_fallback':None if passing else ('already_too_close' if math.dist(client.position()[:2],(target['x'],target['y']))<55.5 else 'blocked_arc')})
            if not points:
                unresolved.append(target['key']);continue
            shops.anchor_positions=[(target['x'],target['y'])];shops.priority_keys={target['key']}
            approach_cap=55 if target['key'] in shops.dynamic_targets else 25
            budget=min(approach_cap,duration-(time.monotonic()-started)-6)
            client.read_goal_key=None if passing else target['key']
            client.pass_goal_key=target['key'] if passing else None
            try:
                if direct:
                    result=follow(client,nav,points,log,budget,progress,None)
                else:
                    result=follow_with_recovery(client,nav,points,log,budget,progress,guard,
                                                max_recoveries=2)
            finally:
                client.read_goal_key=None
                client.pass_goal_key=None
            results.append(result)
            if result['reason'] in STOP_REASONS: break
            if passing and result['reason']=='completed' and target['key'] not in shops.captured_keys:
                # One passing window was missed. Use the existing bounded
                # close approach, not another circle or a wait beyond range.
                log({'type':'radar_pass_read_missed','key':target['key']})
                points,nav,direct=recheck_route(route_data,client.position()[:2],target,
                    client.execution_clearance,navigation=departure)
                remaining=min(approach_cap-(time.monotonic()-approach_started),duration-(time.monotonic()-started)-6)
                if points and remaining>0:
                    client.read_goal_key=target['key']
                    try:
                        result=follow(client,nav,points,log,remaining,progress,None) if direct else follow_with_recovery(
                            client,nav,points,log,remaining,progress,guard,max_recoveries=2)
                    finally: client.read_goal_key=None
                    results.append(result)
                    if result['reason'] in STOP_REASONS: break
            if result['reason']=='completed' and target['key'] not in shops.captured_keys:
                read=hold_read(client,shops,target,log,min(started+duration-3,time.monotonic()+4))
                if read in STOP_REASONS:
                    result['reason']=read;break
                if read=='temporarily_unavailable': result['reason']=read
            if (target['key'] not in shops.captured_keys and target['key'] not in shops.ignored_outside_keys
                    and target['key'] not in shops.unavailable_keys):
                unresolved.append(target['key'])
            handoff=target['key'] in shops.captured_keys
            log({'type':'recheck_result','key':target['key'],'reason':result['reason'],
                 'captured':target['key'] in shops.captured_keys,
                 'seconds':round(time.monotonic()-approach_started,3),
                 'plan_seconds':round(plan_seconds,4)})
    finally:
        shops.dynamic_detours_enabled=old_dynamic
    reason='cancelled' if client.cancelled() else (
        results[-1]['reason'] if results and results[-1]['reason'] in STOP_REASONS else 'completed')
    return {'reason':reason,'seconds':round(time.monotonic()-begun,3),
            'recoveries':[r for result in results for r in result.get('recoveries',[])],
            'position':client.position(),'targets':attempted,'unresolved':unresolved}
