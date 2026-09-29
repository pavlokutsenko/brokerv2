"""Bounded route plans for a UI-configured center and selective price targets."""
import math
import random
from shapely.geometry import Point,LineString,box
from shapely.ops import unary_union
from walk_geometry import Navigation,rounded_route,polygon_rings
from radar_pass_plan import radar_pass_route


# The original mesh sections have three openings in the west temple wall.
# Broad 3D capsule-band projections fill all three even where the sections
# have no wall at walking height. Keep the rest of each native-confirmed band.
def _rings(geometry,origin=(80000,147000)):
    result=[]
    for polygon in getattr(geometry,'geoms',[geometry]):
        if polygon.is_empty or polygon.geom_type!='Polygon': continue
        for ring in (polygon.exterior,*polygon.interiors):
            result.append([[round(x-origin[0],3),round(y-origin[1],3)] for x,y in ring.coords])
    return result


def price_navigation_data(data):
    """Avoid projected roof volumes while retaining observed temple walls.

    The capsule-band projections of these meshes cover the walkable upper
    market plaza and leave no point within shop range. Live native sweeps hit
    side_body, side_body2 and front_body repeatedly, so keep their bands. Their
    original 2D sections also remain; WalkGuard checks every movement command.
    """
    rules=data.get('navigationRules',{})
    origin=data.get('origin',(80000,147000))
    broad_bands=tuple(rules.get('skipProjectionPrefixes',[]))
    observed_walls=set(rules.get('keepProjectionWalls',[]))
    openings=unary_union([box(x1,y1,x2,y2) for x1,x2,y1,y2 in rules.get('gates',[])])
    obstacles=[]
    for obstacle in data['obstacles']:
        name=obstacle['name']
        if (name.endswith(' [capsule band]') and name.startswith(broad_bands)
                and name not in observed_walls):
            continue
        if name in observed_walls:
            original=polygon_rings(obstacle['rings'],origin)
            if original.intersects(openings):
                obstacle={**obstacle,'rings':_rings(original.difference(openings),origin)}
        obstacles.append(obstacle)
    return {**data,'obstacles':obstacles}


def passing_lines(nav,center,radius=72,length=135):
    """Straight 55+ standoff passes; no arc that loops back around a shop."""
    result=[]
    for rotation in range(12):
        angle=rotation*math.tau/12
        forward=(math.cos(angle),math.sin(angle))
        normal=(-forward[1],forward[0])
        for side in (-1,1):
            middle=(center[0]+side*radius*normal[0],center[1]+side*radius*normal[1])
            entry=(middle[0]-length*forward[0],middle[1]-length*forward[1])
            leave=(middle[0]+length*forward[0],middle[1]+length*forward[1])
            if nav.clear(entry,middle) and nav.clear(middle,leave):
                result.append([entry,middle,leave])
    return result


def direct_standoff_lines(nav,center,radius=72,length=90):
    """Reach a shop from one safe side, then keep moving along that side."""
    result=[]
    for rotation in range(24):
        angle=rotation*math.tau/24
        outward=(math.cos(angle),math.sin(angle))
        entry=(center[0]+radius*outward[0],center[1]+radius*outward[1])
        tangent=(-outward[1],outward[0])
        for side in (-1,1):
            leave=(entry[0]+side*length*tangent[0],entry[1]+side*length*tangent[1])
            if nav.clear(entry,leave):
                result.append([entry,leave])
    return result


def shorten_order(start,targets):
    """Remove obvious backtracking in the open path through spaced anchors."""
    ordered=list(targets)
    def distance(a,b): return math.dist(a,b)
    def center(t): return (t['x'],t['y'])
    changed=True
    while changed:
        changed=False
        for i in range(len(ordered)-1):
            previous=start if i==0 else center(ordered[i-1])
            for j in range(i+1,len(ordered)):
                old=distance(previous,center(ordered[i]))
                new=distance(previous,center(ordered[j]))
                if j+1<len(ordered):
                    following=center(ordered[j+1])
                    old+=distance(center(ordered[j]),following)
                    new+=distance(center(ordered[i]),following)
                if new+1e-6<old:
                    ordered[i:j+1]=reversed(ordered[i:j+1])
                    changed=True
                    break
            if changed: break
    return ordered


def center_route(data,start,center,previous=None,rng=None,standing_radius=200):
    rng=rng or random.SystemRandom()
    nav=Navigation(data)
    if not math.isfinite(standing_radius) or not 30<=standing_radius<=500:
        raise ValueError('Invalid center standing radius')

    # Distinct automatically calculated stops keep the whole route, including
    # the follower's 23-unit endpoint tolerance, inside the verified center disk.
    if standing_radius>=120:
        stop_radius=standing_radius-60
        stops=[(center[0]+stop_radius*math.cos(i*math.tau/8),
                center[1]+stop_radius*math.sin(i*math.tau/8)) for i in range(8)]
        stops.append(tuple(center))
        rng.shuffle(stops)
        for stop in stops:
            if previous and math.dist(stop,previous)<125: continue
            if not nav.clear(stop,stop): continue
            for _ in range(12):
                angle=rng.uniform(0,math.tau);radius=25*math.sqrt(rng.random())
                destination=(stop[0]+radius*math.cos(angle),stop[1]+radius*math.sin(angle))
                if previous and math.dist(destination,previous)<100: continue
                if not nav.clear(destination,destination): continue
                try: points=rounded_route(nav.shortest(start,destination),nav)
                except RuntimeError: continue
                return {'points':points,'destination':destination,'centerStop':stop,'anchors':[],'deferred':[]}

    # Some saved centers have no reachable ring stop. Retain the old bounded
    # search so those profiles can still return without relaxing coverage.
    for _ in range(160):
        # The follower accepts a 23-unit endpoint error. Leave a stopping
        # margin so a destination near the outer circle cannot finish outside.
        angle=rng.uniform(0,math.tau);radius=(standing_radius-30)*math.sqrt(rng.random())
        destination=(center[0]+radius*math.cos(angle),center[1]+radius*math.sin(angle))
        if previous and math.dist(destination,previous)<min(100,standing_radius/2): continue
        if not nav.clear(destination,destination): continue
        try: points=rounded_route(nav.shortest(start,destination),nav)
        except RuntimeError: continue
        return {'points':points,'destination':destination,'centerStop':tuple(center),'anchors':[],'deferred':[]}
    raise RuntimeError(f'No reachable center destination inside radius {standing_radius}')


def price_route(data,start,targets,max_anchors=64,anchor_spacing=150,pass_length=90,navigation=None):
    nav=navigation.fork() if navigation is not None else Navigation(data)
    anchors=[];deferred=[];approaches=[];raw=[tuple(start)]
    remaining=list(targets)
    selected=[];cursor=start
    while remaining and len(selected)<max_anchors:
        t=min(remaining,key=lambda t:math.dist(cursor,(t['x'],t['y'])))
        remaining.remove(t);center=(t['x'],t['y'])
        # One pass services neighbors too. Global disks around every shop would
        # close the paths in a dense market; only spaced route anchors get disks.
        if any(math.dist(center,(a['x'],a['y']))<anchor_spacing for a in selected): continue
        if math.dist(start,center)<60:
            deferred.append({'key':t['key'],'reason':'Too close for a passing approach'});continue
        selected.append(t);cursor=center
    selected=shorten_order(start,selected)
    # All anchors participate in every connector, including future anchors.
    # Otherwise a valid connector could cross a later execution exclusion disk.
    # Install the full set once. Rebuilding the navigation grid for every
    # anchor took several seconds while the character stood between batches.
    if selected:
        nav.add_blocker(unary_union([Point(t['x'],t['y']).buffer(55) for t in selected]))
    for index,t in enumerate(selected):
        center=(t['x'],t['y'])
        onward=(selected[index+1]['x'],selected[index+1]['y']) if index+1<len(selected) else None
        if not nav.clear(raw[-1],raw[-1]):
            # Already close: continue outwards; do not route into the trader.
            deferred.append({'key':t['key'],'reason':'Too close for a passing approach'});continue
        # Use the same short forward arc as a radar/revisit approach. Keep all
        # anchor exclusions in the connector, and fall back only where blocked.
        passing_arc=radar_pass_route(data,raw[-1],t,navigation=nav,onward=onward)
        chosen=passing_arc[0] if passing_arc else None
        if chosen is not None:
            raw.extend(chosen[1:]);anchors.append(t)
            approaches.append({'key':t['key'],'mode':'arc','points':chosen[-14:]})
            continue
        options=sorted(passing_lines(nav,center,length=pass_length),key=lambda p:
            math.dist(raw[-1],p[0])+(math.dist(p[-1],onward) if onward else 0))
        chosen=None
        best=float('inf')
        for passing in options[:8]:
            try: connector=nav.shortest(raw[-1],passing[0])
            except RuntimeError: continue
            candidate=rounded_route([*connector,*passing[1:]],nav)
            distance=LineString(candidate).distance(Point(center))
            if not 54.9<=distance<=85: continue
            cost=sum(math.dist(a,b) for a,b in zip(candidate,candidate[1:]))
            if onward: cost+=math.dist(candidate[-1],onward)
            if cost<best: chosen=candidate;best=cost
        if chosen is None:
            # A dense block may have no full crossing line, although one
            # direct side is reachable. Keep the same 55+ standoff and native
            # movement guard; use a short tangent instead of circling a shop.
            direct=sorted(direct_standoff_lines(nav,center),key=lambda p:
                math.dist(raw[-1],p[0])+(math.dist(p[-1],onward) if onward else 0))
            for passing in direct[:12]:
                try: connector=nav.shortest(raw[-1],passing[0])
                except RuntimeError: continue
                candidate=rounded_route([*connector,*passing[1:]],nav)
                distance=LineString(candidate).distance(Point(center))
                if not 54.9<=distance<=85: continue
                cost=sum(math.dist(a,b) for a,b in zip(candidate,candidate[1:]))
                if onward: cost+=math.dist(candidate[-1],onward)
                if cost<best: chosen=candidate;best=cost
        if chosen is None:
            deferred.append({'key':t['key'],'reason':'No clear passing route'});continue
        raw.extend(chosen[1:]);anchors.append(t)
        approaches.append({'key':t['key'],'mode':'blocked_arc_fallback'})
    if len(raw)<2: return {'points':[],'anchors':anchors,'blockers':selected,'deferred':deferred,'approaches':approaches}
    return {'points':raw,'anchors':anchors,'blockers':selected,'deferred':deferred,'approaches':approaches}
