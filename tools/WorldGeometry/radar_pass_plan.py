"""A tangent entry and short forward arc for a broker or radar shop."""
import math
from shapely.geometry import LineString,Point
from walk_geometry import Navigation,rounded_route


def radar_pass_route(data,start,target,clearance=45,navigation=None,onward=None):
    center=(target['x'],target['y']);distance=math.dist(start,center);radius=72
    if distance<55.5: return None  # Already beside it: read now, never loop back.
    nav=navigation.fork() if navigation is not None else Navigation(data,clearance=clearance)
    # Prevent both the connector and lookahead from cutting across the shop.
    nav.add_blocker(Point(center).buffer(55))
    angle=math.atan2(start[1]-center[1],start[0]-center[0])
    # Nearby shops also need a forward passing path. Being in request range
    # does not justify a stationary close approach for each broker target.
    tangent=math.acos(radius/distance) if distance>95 else 0
    choices=[]
    for side in (-1,1):
        first=angle+side*tangent
        arc=[(center[0]+radius*math.cos(first+side*i*math.pi/48),
              center[1]+radius*math.sin(first+side*i*math.pi/48)) for i in range(13)]
        last=first+side*math.pi/4
        leave=(arc[-1][0]-side*110*math.sin(last),arc[-1][1]+side*110*math.cos(last))
        passing=[*arc,leave]
        if not all(nav.clear(a,b) for a,b in zip(passing,passing[1:])): continue
        try: connector=rounded_route(nav.shortest(start,arc[0]),nav)
        except RuntimeError: continue
        points=[*connector,*passing[1:]]
        line=LineString(points)
        if line.distance(Point(center))<55: continue
        cost=line.length+(math.dist(leave,onward) if onward is not None else 0)
        choices.append((cost,points))
    return (min(choices,key=lambda item:item[0])[1],nav) if choices else None
