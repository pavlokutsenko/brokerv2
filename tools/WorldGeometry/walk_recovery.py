"""Bounded local avoidance and rejoining of a coverage route."""
from bisect import bisect_right
import math
import time
from shapely.geometry import Point
from walk_follow import Path,follow
from walk_geometry import Navigation,rounded_route
from walk_escape import escape
from cycle_recheck_plan import recheck_route
from radar_pass_plan import radar_pass_route


def rejoin(nav,current,path,arc,minimum=160):
    for ahead in (minimum,minimum+160,minimum+400,minimum+800):
        index=min(len(path.points)-1,bisect_right(path.distance,arc+ahead))
        try:
            connector=nav.shortest(current,path.points[index])
            connector=rounded_route(connector,nav)
        except RuntimeError:
            continue
        return [*connector,*path.points[index+1:]],path.distance[index]-arc
    raise RuntimeError('no local detour/rejoin available')


def learn(nav,current,goal,hit):
    if hit and hit.get('point'):
        point=tuple(hit['point'][:2])
    else:
        distance=math.dist(current,goal)
        point=tuple(current[k]+(goal[k]-current[k])*min(40,distance)/max(distance,1) for k in (0,1))
    distance=math.dist(current,point)
    # Never trap the current capsule inside a newly learned exclusion circle.
    radius=min(42,max(0,distance-7))
    if radius<6:
        return None
    nav.add_blocker(Point(point).buffer(radius))
    return {'point':point,'radius':radius,'source':hit}


def unstick(client,nav,guard,log,deadline):
    """Try a few short clear departures and require real pawn movement."""
    source=client.position()
    directions=((-1,0),(0,-1),(0,1),(1,0),(-.707,-.707),(-.707,.707),(.707,-.707),(.707,.707))
    for dx,dy in directions:
        if client.cancelled() or time.monotonic()+1>=deadline:
            return False
        current=client.position()
        goal=(current[0]+65*dx,current[1]+65*dy)
        if not nav.clear(current[:2],goal):
            log({'type':'unstick_skip','reason':'map','source':current,'goal':goal})
            continue
        if not guard.clear(current,goal):
            log({'type':'unstick_skip','reason':'capsule','source':current,'goal':goal,
                 'hit':guard.last_hit})
            continue
        client.move(goal)
        start=time.monotonic()
        while time.monotonic()-start<1.1:
            observed=client.position()
            if math.dist(source[:2],observed[:2])>=12:
                client.stop()
                log({'type':'unstick_success','source':source,'goal':goal,
                     'position':client.position()})
                return True
            time.sleep(.08)
        client.stop()
        log({'type':'unstick_no_motion','source':source,'goal':goal})
    return False


def follow_with_recovery(client,nav,points,log,max_seconds,progress_callback,guard,
                         dynamic_route_data=None,max_recoveries=12):
    started=time.monotonic();remaining=points;events=[];segments=[];zones={};unstick_zones=set()
    radar_navigation=Navigation(dynamic_route_data,clearance=client.execution_clearance) if dynamic_route_data is not None else None
    while True:
        local=Path(remaining)
        result=follow(client,nav,remaining,log,max(0,max_seconds-(time.monotonic()-started)),progress_callback,guard)
        segments.append(result)
        if result['reason']=='radar_detour' and dynamic_route_data is not None:
            shops=client.shops
            target=shops.detour_target
            current=client.position()[:2]
            budget=max_seconds-(time.monotonic()-started)
            try:
                planned=time.monotonic()
                passing=radar_pass_route(dynamic_route_data,current,target,
                            client.execution_clearance,navigation=radar_navigation,
                            onward=local.at(result['progress']+350))
                if passing:
                    approach,detour_nav=passing;direct=False
                else:
                    approach,detour_nav,direct=recheck_route(dynamic_route_data,current,target,
                                client.execution_clearance,navigation=radar_navigation)
                plan={'points':approach}
                log({'type':'radar_detour_plan','key':target['key'],
                     'seconds':round(time.monotonic()-planned,4),'direct_probe':direct,
                     'passing_arc':bool(passing),'points':approach})
                # A radar detour can finish inside a different main-route
                # anchor's exclusion disk. Validate its return before leaving.
                if plan['points']:
                    try:
                        rejoin(nav,plan['points'][-1],local,result['progress'],minimum=0)
                    except RuntimeError:
                        # Read at the endpoint first. An immediate reverse tail
                        # lets lookahead turn back before entering read range.
                        # The actual return below already escapes anchor disks.
                        log({'type':'radar_detour_rejoin_needs_escape','key':target['key']})
                detour_length=Path(plan['points']).distance[-1] if plan['points'] else float('inf')
            except RuntimeError:
                plan=None;detour_length=float('inf')
            if budget>=12 and detour_length<=6000:
                log({'type':'radar_detour_start','key':target['key'],
                     'planned_length':round(detour_length,1)})
                shops.dynamic_detours_enabled=False
                old_anchors=shops.anchor_positions
                old_priority=getattr(shops,'priority_keys',set())
                shops.priority_keys={target['key']}
                shops.anchor_positions=[*old_anchors,(target['x'],target['y'])]
                old_goal=getattr(client,'read_goal_key',None)
                old_pass=getattr(client,'pass_goal_key',None)
                client.read_goal_key=None if passing else target['key']
                client.pass_goal_key=target['key'] if passing else None
                try:
                    if direct:
                        detour=follow(client,detour_nav,plan['points'],log,min(55,budget-5),progress_callback,None)
                    else:
                        detour=follow_with_recovery(client,detour_nav,plan['points'],log,
                            min(55,budget-5),progress_callback,guard,max_recoveries=2)
                finally:
                    shops.dynamic_detours_enabled=True
                    shops.anchor_positions=old_anchors
                    shops.priority_keys=old_priority
                    client.read_goal_key=old_goal
                    client.pass_goal_key=old_pass
                segments.append(detour)
                log({'type':'radar_detour_result','key':target['key'],
                     'reason':detour['reason'],'requested':target['key'] in shops.requested_keys})
                result['position']=client.position()
                if detour['reason'] in ('cancelled','keyboard_interrupt',
                                        'target_changed_externally','position_jump'):
                    result=detour;break
            else:
                log({'type':'radar_detour_skipped','key':target['key'],
                     'reason':'no bounded route or time'})
            try:
                position=client.position()
                if not nav.clear(position[:2],position[:2]):
                    if not escape(client,nav,guard,log,started+max_seconds):
                        raise RuntimeError('could not leave main-route anchor clearance')
                remaining,skipped=rejoin(nav,client.position()[:2],local,result['progress'],minimum=0)
                log({'type':'radar_detour_rejoin','key':target['key'],
                     'replaced_route_length':skipped})
                continue
            except RuntimeError as error:
                result['reason']='radar_rejoin_unreachable';result['error']=str(error);break
        if result['reason'] not in ('blocked','stalled','unsafe_shortcut','route_deviation'):
            break
        if dynamic_route_data is not None:
            # The main market path has become stale. Stop and let the caller
            # rebuild individual unread approaches instead of skipping large
            # portions of the old route through repeated recovery loops.
            log({'type':'route_replan_required','trigger':result['reason'],
                 'position':client.position(),'progress':result['progress']})
            result['reason']='replan_required';break
        if client.cancelled():
            result['reason']='cancelled';break
        current=client.position()[:2]
        zone=tuple(round(v/120) for v in current)
        if result['reason']=='stalled' and zone not in unstick_zones:
            unstick_zones.add(zone)
            if unstick(client,nav,guard,log,started+max_seconds):
                try:
                    remaining,skipped=rejoin(nav,client.position()[:2],local,result['progress'])
                    log({'type':'unstick_rejoin','replaced_route_length':skipped})
                    continue
                except RuntimeError:
                    pass
            result['reason']='immobile';break
        zones[zone]=zones.get(zone,0)+1
        if len(events)>=max_recoveries or zones[zone]>min(4,max_recoveries):
            result['reason']='recovery_limit';break
        arc=result['progress'];goal=local.at(arc+100)
        hit=guard.last_hit if result['reason']=='blocked' else None
        blocker=learn(nav,current,goal,hit)
        if not nav.clear(current,current):
            if not escape(client,nav,guard,log,started+max_seconds):
                result['reason']='escape_unreachable';break
            current=client.position()[:2]
        try:
            remaining,skipped=rejoin(nav,current,local,arc,500 if zones[zone]>=3 else 160)
        except RuntimeError as error:
            result['reason']='recovery_unreachable';result['error']=str(error);break
        event={'type':'recovery','attempt':len(events)+1,'trigger':result['reason'],
               'position':current,'blocker':blocker,'replaced_route_length':skipped,
               'deferred':zones[zone]>=3,'points':remaining[:min(35,len(remaining))]}
        events.append(event);log(event)
        if progress_callback:
            progress_callback({k:v for k,v in event.items() if k not in ('points','blocker')})
    return {**result,'seconds':round(time.monotonic()-started,3),
            'commands':sum(s['commands'] for s in segments),'traveled':round(sum(s['traveled'] for s in segments),2),
            'peak_route_error':max(s['peak_route_error'] for s in segments),
            'recoveries':events,'segments':segments,'capsule_queries':guard.queries,
            'capsule_step_overrides':getattr(guard,'step_overrides',0)}
