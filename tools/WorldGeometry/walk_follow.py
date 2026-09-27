"""Continuous look-ahead follower with progress, corridor and cancellation guards."""
from bisect import bisect_right
from collections import deque
import math
import time
from walk_arrival_read import arrival_read


def temple_gate_direct_allowed(current, goal, hit, rules=None):
    """Let the game attempt one short passage through a modelled arch.

    The exported capsule sweep hits the temple facade at the three visible
    arches. Game movement is the final check; the follower still requires
    observed progress and the recovery loop bounds a failed attempt.
    """
    rules=rules or {}
    if not hit or hit.get('outer') not in rules.get('gateFacadeMeshes',[]):
        return False
    point=hit.get('point')
    normal=hit.get('normal') or (0,0,0)
    band=rules.get('gateImpactX')
    if not point or not band or not band[0]<=point[0]<=band[1]:
        return False
    gates=[(gate[2],gate[3]) for gate in rules.get('gates',[])]
    # A real central-gate exit hit the sloped tread of the facade with
    # normal(.774,-.286,.565), while the pawn continued across it. Keep this
    # extra ordinary probe confined to the middle of an arch, facing across
    # the facade, with the commanded goal on its centerline.
    horizontal=math.hypot(*normal[:2])
    central_tread=(.4<=normal[2]<.8 and horizontal>.5
        and abs(normal[0])/horizontal>=.9
        and any(abs(point[1]-(low+high)/2)<=35
                and abs(goal[1]-(low+high)/2)<=10 for low,high in gates))
    if abs(normal[0])<.8 and not central_tread:
        return False
    # A capsule touches the facade before its center reaches the impact point.
    # Permit an endpoint within one capsule radius of that point as well.
    crosses=(current[0]<point[0]-2 and goal[0]>=point[0]-12 and goal[0]>current[0]) or \
            (current[0]>point[0]+2 and goal[0]<=point[0]+12 and goal[0]<current[0])
    return (crosses and
            math.dist(current[:2],goal)<=160 and
            any(low<=point[1]<=high for low,high in gates))


class Path:
    def __init__(self,points):
        self.points=[tuple(p) for p in points]
        self.distance=[0.0]
        for a,b in zip(points,points[1:]):
            self.distance.append(self.distance[-1]+math.dist(a,b))

    def at(self,arc):
        arc=max(0,min(arc,self.distance[-1]))
        i=max(0,min(bisect_right(self.distance,arc)-1,len(self.points)-2))
        t=(arc-self.distance[i])/max(self.distance[i+1]-self.distance[i],1e-9)
        return tuple(self.points[i][j]+t*(self.points[i+1][j]-self.points[i][j]) for j in (0,1))

    def project(self,p,previous):
        first=max(0,bisect_right(self.distance,max(0,previous-35))-1)
        last=min(len(self.points)-1,bisect_right(self.distance,previous+160))
        best=(float('inf'),previous,float('inf'))
        for i in range(first,last):
            a,b=self.points[i:i+2]
            d=(b[0]-a[0],b[1]-a[1]);length=math.hypot(*d)
            t=max(0,min(1,((p[0]-a[0])*d[0]+(p[1]-a[1])*d[1])/max(length*length,1e-9)))
            arc=self.distance[i]+t*length
            error=math.dist(p,(a[0]+t*d[0],a[1]+t*d[1]))
            score=error+.015*max(0,arc-previous)
            if score<best[0]:
                best=(score,arc,error)
        return best[1:]


def shop_lookahead(current,shops,ordinary,rules=None):
    # Stay on the gate centerline while crossing the short facade/steps.
    # Otherwise a speed-based endpoint can exceed the bounded direct probe
    # (160 units) and turn a valid entrance into a learned obstacle.
    rules=rules or {}
    band=rules.get('gateApproachX')
    if band and band[0] <= current[0] <= band[1] and any(
            gate[2]-160 <= current[1] <= gate[3]+160 for gate in rules.get('gates',[])):
        return min(ordinary,95)
    if shops and any(math.dist(current[:2],point)<220
                     for point in getattr(shops,'anchor_positions',())):
        return min(ordinary,95)
    return ordinary


def gate_center_goal(current,goal,rules=None):
    """Do not round an entrance crossing into the temple's side pillars."""
    rules=rules or {};band=rules.get('gateApproachX')
    if not band or not band[0]<=current[0]<=band[1] or abs(goal[0]-current[0])<20:
        return goal
    for gate in rules.get('gates',[]):
        if gate[2]<=current[1]<=gate[3] and (
                min(current[0],goal[0])<=gate[1] and max(current[0],goal[0])>=gate[0]):
            return (goal[0],(gate[2]+gate[3])/2)
    return goal


def lookahead_goal(path,nav,current,arc,lookahead,rules=None):
    """Do not repeatedly send a folded path's already reached near endpoint."""
    while lookahead>=28:
        candidate=gate_center_goal(current,path.at(arc+lookahead),rules)
        # Ordinary movement can accept a destination a few units away without
        # moving the pawn. A hairpin can collapse a long arc to such a point.
        # Try a different clear goal, or replan immediately instead of waiting
        # for the immobility watchdog. Keep final arrival for the outer loop,
        # which may have entered before the latest position reached the goal.
        final=arc+lookahead>=path.distance[-1]
        if (final or math.dist(current[:2],candidate)>=8) and nav.clear(tuple(current[:2]),candidate):
            return candidate
        lookahead*=.75
    return None


def follow(client,nav,points,log,max_seconds=600,progress_callback=None,guard=None):
    path=Path(points)
    start=time.monotonic();arc=0.0;last_sent=0;last_log=0;last_goal=None
    history=deque();commands=0;peak_error=0;last_report=0
    gate_probe_until=0;gate_probe_commands=0
    reason="completed";current=client.position();traveled=0.0;last=current
    last_position_at=start
    shops=getattr(client,'shops',None)
    if shops:
        shops.active.set()
    individual=bool(shops and getattr(client,'read_goal_key',None))
    pass_key=getattr(client,'pass_goal_key',None)
    end_error=8 if individual else 23
    # Both arrival checks must agree. At a game-accepted endpoint5.58units
    # away, an8-unit position tolerance plus5-unit arc tolerance kept sending
    # no-op moves until stalled, delaying an otherwise readable nearby shop.
    end_arc=end_error
    # A shop connector ending 68 units away must not stop 23 units early.
    # Reach the planned reading range while ordinary moves can still send
    # pending shop requests, reducing stationary read retries.
    while arc<path.distance[-1]-end_arc or math.dist(current[:2],path.points[-1])>end_error:
        now=time.monotonic()
        if client.cancelled():
            reason="cancelled";break
        if shops and shops.error:
            log({'type':'shop_error','error':shops.error})
            reason='shop_error';break
        goal_key=getattr(client,'read_goal_key',None) or pass_key
        if shops and goal_key in getattr(shops,'unavailable_keys',set()):
            log({'type':'approach_shop_closed','key':goal_key})
            reason='temporarily_unavailable';break
        if shops and getattr(client,'read_goal_key',None) in getattr(shops,'captured_keys',set()):
            log({'type':'approach_read_complete','key':client.read_goal_key})
            break
        if shops and getattr(client,'read_goal_key',None) in getattr(shops,'ignored_outside_keys',set()):
            reason='outside_collection_zone';break
        if now-start>=max_seconds:
            reason="duration_limit";break
        current=client.position()
        observed_at=time.monotonic()
        step=math.dist(current[:2],last[:2])
        # A capsule probe or shop action can make an observation late while
        # ordinary movement continues. Bound speed, not distance per loop.
        if step>max(120,180*(observed_at-last_position_at)+25):
            log({'type':'position_jump','distance':round(step,2),
                 'sample_gap':round(observed_at-last_position_at,3),
                 'previous':last,'position':current})
            reason="position_jump";break
        traveled+=step;last=current;last_position_at=observed_at
        arc,error=path.project(current[:2],arc)
        peak_error=max(peak_error,error)
        history.append((now,current,arc))
        while len(history)>1 and now-history[1][0]>2.8:
            history.popleft()
        if error>85:
            reason="route_deviation";break
        if now-history[0][0]>=2.8 and math.dist(current[:2],history[0][1][:2])<10 and arc-history[0][2]<10:
            reason="stalled";break
        selected=client.m.u64(client.world["controller"]+0x898)
        if selected!=client.initial_selected and not (shops and shops.allowed_target(selected)):
            reason="target_changed_externally";break
        if now-last_log>=.1:
            log({"type":"sample","t":round(now-start,3),"position":current,"arc":round(arc,2),"error":round(error,2)})
            last_log=now
        if now-last_report>=10 and progress_callback:
            progress_callback({"seconds":round(now-start),"percent":round(100*arc/path.distance[-1],1),"commands":commands,
                               **({'shops':shops.stats['captured_shops'],'exact':shops.stats['exact_shops'],
                                   'rows':shops.stats['rows']} if shops else {})})
            last_report=now
        if shops and getattr(shops,'claim_dynamic_detour',lambda *_:None)(current,path,arc):
            reason='radar_detour';break
        resume=bool(shops and shops.needs_resume())
        if now-last_sent>=.20 or resume:
            speed=math.dist(current[:2],history[0][1][:2])/max(now-history[0][0],.2)
            lookahead=max(125,min(210,100+speed*.6))
            # A long clear shortcut can cut across an entire shop pass even
            # though its planned line comes within interaction range. Follow
            # the rounded path more closely only beside price anchors.
            lookahead=shop_lookahead(current,shops,lookahead,getattr(client,'navigation_rules',{}))
            goal=lookahead_goal(path,nav,current,arc,lookahead,getattr(client,'navigation_rules',{}))
            if goal is None:
                log({'type':'lookahead_replan','position':current,'arc':arc,
                     'lookahead':lookahead,'reason':'no clear movement goal at least 8 units away'})
                reason="unsafe_shortcut";break
            if last_goal is None or math.dist(last_goal,goal)>18 or now-last_sent>.7 or resume:
                if guard and not guard.clear(current,goal):
                    hit=guard.last_hit
                    gate=temple_gate_direct_allowed(current,goal,hit,getattr(client,'navigation_rules',{}))
                    if gate and (gate_probe_until==0 or now<gate_probe_until) and gate_probe_commands<5:
                        if gate_probe_until==0:
                            gate_probe_until=now+3.5
                            log({'type':'gate_direct_probe','position':current,'goal':goal,'hit':hit})
                        gate_probe_commands+=1
                    else:
                        log({"type":"blocked","position":current,"goal":goal,"hit":hit,
                             'gate_probe_commands':gate_probe_commands})
                        reason="blocked";break
                if client.cancelled():
                    reason="cancelled";break
                if time.monotonic()-start>=max_seconds:
                    reason="duration_limit";break
                log({'type':'move_command','t':round(time.monotonic()-start,3),'position':current,'goal':goal})
                client.move(goal)
                last_sent=time.monotonic();last_goal=goal;commands+=1
        time.sleep(.045)
    if individual and reason=='completed' and client.read_goal_key not in shops.captured_keys:
        read=arrival_read(client,shops,client.read_goal_key,log,
                          max(0,min(.75,max_seconds-(time.monotonic()-start))))
        if read in ('cancelled','shop_error','target_changed_externally'):
            reason=read
    handoff=bool(shops and reason=='completed' and
        (getattr(client,'read_goal_key',None) or pass_key) in shops.captured_keys)
    planning_pause=reason in ('radar_detour','temporarily_unavailable') and callable(getattr(client,'pause_for_plan',None))
    if handoff:
        # The next connector supplies ordinary movement immediately after
        # the exact reply. Final cleanup still performs a verified stop.
        stop2=client.position();drift=None
    elif planning_pause:
        client.pause_for_plan();stop2=client.position();drift=None
    else:
        if shops: shops.active.clear()
        client.stop()
    if not handoff and individual and reason=='completed':
        # The next approach validates its own current origin. Fixed drift
        # sampling belongs to final route/center stops, not every nearby read.
        time.sleep(.1)
        stop2=client.position();drift=None
    elif not handoff and not planning_pause:
        time.sleep(.5)
        stop1=client.position();time.sleep(.4);stop2=client.position()
        drift=math.dist(stop1[:2],stop2[:2])
    result={"reason":reason,"seconds":round(time.monotonic()-start,3),"commands":commands,
            "progress":round(arc,2),"planned_length":round(path.distance[-1],2),"traveled":round(traveled,2),
            "peak_route_error":round(peak_error,2),"position":stop2,"stop_drift":drift}
    result['handoff']=handoff
    result['planning_pause']=planning_pause
    log({"type":"result",**result})
    return result
