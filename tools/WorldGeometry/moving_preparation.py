"""Use a guarded short approach while offline planning catches a late queue change."""
import json
import math
import time
from pathlib import Path
from radar_pass_plan import radar_pass_route
from route_preparation import accept_prepared
from walk_recovery import follow_with_recovery


def prepare_while_moving(client,config,data,route_data,targets,navigation,guard,log,progress):
    request=config.get('preparationInput');output=config.get('preparationOutput')
    if not config.get('continuousSession') or not request or not output or not targets:
        return None
    from cycle_route import write
    shops=client.shops;attempted=set();begun=time.monotonic();movement=0;hops=0
    pause_baseline=getattr(shops,'pause_seconds_spent',0)
    def active_elapsed():
        return time.monotonic()-begun-(getattr(shops,'pause_seconds_spent',0)-pause_baseline)
    def candidate_route():
        start=client.position()[:2]
        for target in targets:
            if target['key'] in attempted or target['key'] in shops.captured_keys or target['key'] in getattr(shops,'unavailable_keys',set()):
                continue
            if math.dist(start,(target['x'],target['y']))<55.5:continue
            passing=radar_pass_route(route_data,start,target,navigation=navigation[1])
            if passing:return target,passing
        return None
    def read_plan():
        try:
            cached=json.loads(Path(output).read_text(encoding='utf-8'))
            return accept_prepared({**config,'preparedPlan':cached},data,client.position()[:2],
                client.execution_clearance,navigation,skip_keys=shops.captured_keys)
        except (OSError,ValueError):return None
    chosen=candidate_route()
    if chosen is None:return None
    write(Path(request),{'city':config['city'],'planningStart':client.position()[:2],
                        'targets':config['targets'],'preparedPlan':config.get('preparedPlan')})
    previous_dynamic=shops.dynamic_detours_enabled;shops.dynamic_detours_enabled=False
    previous_handoff=getattr(client,'preparation_ready',None)
    client.preparation_ready=lambda:Path(output).is_file()
    prepared=None
    try:
        # Continue productive short approaches while the current plan is still
        # computing. A close first shop alone can finish before the planner.
        while chosen and hops<5 and active_elapsed()<25 and not client.cancelled():
            candidate,passing=chosen;attempted.add(candidate['key']);hops+=1
            shops.anchor_positions=[(candidate['x'],candidate['y'])]
            shops.priority_keys={candidate['key']};shops.coverage_anchor_keys={candidate['key']}
            client.pass_goal_key=candidate['key']
            log({'type':'moving_preparation_start','key':candidate['key'],'position':client.position(),'hop':hops})
            log({'type':'recheck_plan','key':candidate['key'],'passing_arc':True,'points':passing[0],
                 'direct_probe':False,'plan_seconds':0,'source':'moving_preparation'})
            walking=time.monotonic()
            paused_before=getattr(shops,'pause_seconds_spent',0)
            result=follow_with_recovery(client,passing[1],passing[0],log,max(0,25-active_elapsed()),
                                       progress,guard,max_recoveries=2)
            movement+=time.monotonic()-walking-(getattr(shops,'pause_seconds_spent',0)-paused_before)
            if result['reason'] in ('cancelled','keyboard_interrupt','target_changed_externally','position_jump','shop_error'):
                return result
            prepared=read_plan()
            if prepared:break
            chosen=candidate_route()
    finally:
        client.pass_goal_key=None;shops.dynamic_detours_enabled=previous_dynamic
        client.preparation_ready=previous_handoff
    wait=time.monotonic()
    while prepared is None and time.monotonic()-wait<5 and not client.cancelled():
        prepared=read_plan()
        if prepared:break
        time.sleep(.03)
    if client.cancelled():return {'reason':'cancelled'}
    evidence={'movementSeconds':round(movement,3),'stationaryWaitSeconds':round(time.monotonic()-wait,3),'hops':hops}
    log({'type':'moving_preparation_result','keys':sorted(attempted),**evidence,
         'accepted':prepared is not None,'capturedKeys':sorted(attempted&shops.captured_keys)})
    if prepared:prepared[0]['movingPreparation']=evidence
    return prepared
