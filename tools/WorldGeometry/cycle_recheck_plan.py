"""An individual approach after stopping, independent of shared anchor disks."""
import math
from shapely.geometry import LineString, Point
from walk_geometry import Navigation, rounded_route, polygon_rings


def recheck_route(data, start, target, clearance=24, navigation=None):
    center=(target['x'],target['y'])
    distance=math.dist(start,center)
    nav=navigation.fork() if navigation is not None else Navigation(data,clearance=clearance)
    if 20<=distance<=75 and nav.clear(start,start):
        return [tuple(start),tuple(start)],nav,False
    angle=math.atan2(start[1]-center[1],start[0]-center[0])
    all_goals=[]
    # User permits closer approaches when a normal passing path is impossible.
    for radius in (68,40,25):
        goals=[(center[0]+radius*math.cos(angle+i*math.tau/24),
                center[1]+radius*math.sin(angle+i*math.tau/24)) for i in range(24)]
        all_goals.extend(goals)
        choices=[]
        for goal in sorted(goals,key=lambda p:math.dist(start,p)):
            # Euclidean distance is a lower bound for every remaining route.
            # The first radial clear approach is already the shortest option.
            if choices and min(item[0] for item in choices)<=math.dist(start,goal)+1e-6:
                break
            try: points=rounded_route(nav.shortest(start,goal),nav)
            except RuntimeError: continue
            line=LineString(points)
            if line.distance(Point(center))<min(radius*.8,max(0,distance-1)):
                continue
            choices.append((line.length,points))
        if choices:
            return min(choices,key=lambda item:item[0])[1],nav,False
    # A floor/roof projection can fill the whole room containing a live shop.
    # Probe that room using the rest of the city geometry, so the connector
    # still goes around the plaza's real partitions instead of running at them.
    projections=[o for o in data['obstacles'] if o.get('name','').endswith(' [capsule band]')
                 and polygon_rings(o['rings'],data.get('origin',(80000,147000))).distance(Point(center))<75]
    if projections:
        relaxed=Navigation({**data,'ground':None,
                            'obstacles':[o for o in data['obstacles'] if o not in projections]},
                           clearance=clearance)
        candidates=[]
        for goal in sorted(all_goals,key=lambda p:math.dist(start,p)):
            try: points=rounded_route(relaxed.shortest(start,goal),relaxed)
            except RuntimeError: continue
            candidates.append((LineString(points).length,points))
        if candidates:
            return min(candidates,key=lambda item:item[0])[1],relaxed,True
    # Normal direct movement lets the game resolve inaccurate projected geometry.
    # This is a short bounded probe, never a teleport.
    if distance<=8000:  # Known broker targets can lie beyond the radar detour radius.
        ox,oy=data.get('origin',(80000,147000))
        xs=[p[0]-ox for p in (start,*all_goals)]
        ys=[p[1]-oy for p in (start,*all_goals)]
        e=data['extent']
        # The captured map is only a geometry survey, not the market boundary.
        # Outside it use bounded ordinary game movement, not invented walls.
        extent=[min(e[0],min(xs)-60),max(e[1],max(xs)+60),
                min(e[2],min(ys)-60),max(e[3],max(ys)+60)]
        probe=Navigation({**data,'extent':extent,'obstacles':[],
                          'unknown':[],'ground':None},clearance=0)
        for goal in sorted(all_goals,key=lambda p:math.dist(start,p)):
            if probe.clear(start,goal):
                return [tuple(start),goal],probe,True
            try: points=rounded_route(probe.shortest(start,goal),probe)
            except RuntimeError: continue
            return points,probe,True
    return [],nav,False
