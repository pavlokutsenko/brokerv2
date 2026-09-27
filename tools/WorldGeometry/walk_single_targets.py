"""Four individual fly-bys: tangent entry, curved pass, onward departure."""
from datetime import datetime
import json
import math
import numpy as np
from shapely.geometry import LineString, Point

from walk_geometry import Navigation, rounded_route
from walk_plan import nearby_segments
from walk_shop_policy import trader_key

PASS_RADIUS=72.0
PLANNING_STANDOFF=55.0
EXECUTION_STANDOFF=45.0


def flybys(nav,center):
    """Curves do not terminate at the trader; both tangents extend by 170 units."""
    result=[]
    for rotation in range(12):
        angle=rotation*math.tau/12
        for direction in (-1,1):
            angles=[angle+direction*math.radians(-65+13*i) for i in range(11)]
            arc=[(center[0]+PASS_RADIUS*math.cos(a),center[1]+PASS_RADIUS*math.sin(a)) for a in angles]
            tangents=[(-direction*math.sin(a),direction*math.cos(a)) for a in (angles[0],angles[-1])]
            entry=tuple(arc[0][k]-170*tangents[0][k] for k in (0,1))
            leave=tuple(arc[-1][k]+170*tangents[1][k] for k in (0,1))
            points=[entry,*arc,leave]
            if all(nav.clear(a,b) for a,b in zip(points,points[1:])):
                result.append(points)
    return result


def add_exclusions(nav,targets,radius=EXECUTION_STANDOFF):
    for target in targets:
        nav.add_blocker(Point(target['x'],target['y']).buffer(radius))


def plan_singles(data,start,traders):
    nav=Navigation(data)
    live=[t for t in traders if nav.region.covers(Point(t['x'],t['y'])) and math.dist(start,(t['x'],t['y']))>240]
    if len(live)<4:
        raise RuntimeError('not enough live traders inside the mapped market')
    xy=np.array([(t['x'],t['y']) for t in live])
    # Prefer visually separated traders near four corners, without assuming the
    # single observer currently sees every shop on the server.
    q=np.quantile(xy,[.04,.96],axis=0);middle=np.median(xy,axis=0)
    corners=[(q[0,0],q[0,1]),(q[1,0],q[0,1]),(q[1,0],q[1,1]),(q[0,0],q[1,1])]
    names=['north-west','north-east','south-east','south-west']
    first=min(range(4),key=lambda i:math.dist(start,corners[i]))
    order=[(i+first)%4 for i in range(4)]
    selected=[]
    for index in order:
        corner=corners[index]
        mask=(xy[:,0]<middle[0])==(corner[0]<middle[0])
        mask&=(xy[:,1]<middle[1])==(corner[1]<middle[1])
        scores=[]
        for i in np.flatnonzero(mask):
            crowd=int((np.sum((xy-xy[i])**2,axis=1)<100**2).sum())-1
            scores.append((math.dist(xy[i],corner)+40*min(crowd,20),int(i),crowd))
        found=None
        for _,i,crowd in sorted(scores)[:30]:
            if flybys(nav,xy[i]):
                found={**live[i],'trader_key':trader_key(live[i]['name']),
                       'corner':names[index],'neighbors_within_100':crowd}
                break
        if found is None:
            raise RuntimeError(f'no guarded fly-by available in {names[index]}')
        selected.append(found)
    add_exclusions(nav,selected,PLANNING_STANDOFF)
    raw=[tuple(start)]
    for index,target in enumerate(selected):
        options=flybys(nav,(target['x'],target['y']))
        onward=(selected[index+1]['x'],selected[index+1]['y']) if index+1<len(selected) else start
        ranked=sorted(options,key=lambda p:math.dist(raw[-1],p[0])+math.dist(p[-1],onward))
        best=None
        for points in ranked[:6]:
            try:
                connector=nav.shortest(raw[-1],points[0])
            except RuntimeError:
                continue
            cost=sum(math.dist(a,b) for a,b in zip(connector,connector[1:]))+math.dist(points[-1],onward)
            if best is None or cost<best[0]:
                best=cost,connector,points
        if best is None:
            raise RuntimeError(f"cannot connect to fly-by for {target['name']}")
        _,connector,points=best
        raw.extend(connector[1:]);raw.extend(points[1:])
        target['entry']=points[0];target['leave']=points[-1]
    raw.extend(nav.shortest(raw[-1],start)[1:])
    points=rounded_route(raw,nav)
    line=LineString(points)
    for target in selected:
        target['planned_min_distance']=line.distance(Point(target['x'],target['y']))
        if not PLANNING_STANDOFF-.1<=target['planned_min_distance']<=85:
            raise RuntimeError('fly-by standoff/range validation failed')
    old=np.array([[t[1]+80000,t[2]+147000] for t in data['traders']])
    return {'mode':'single_target_flybys','source_capture':data['captured'],'start':start,
            'radius':125,'clearance':55,'points':points,'control_points':raw,'visits':len(selected),
            'targets':selected,'standoff':EXECUTION_STANDOFF,'pass_radius':PASS_RADIUS,
            'length':sum(math.dist(a,b) for a,b in zip(points,points[1:])),
            'traders':len(old),'planned_nearby':int(nearby_segments(old,points,125).sum())}


def target_report(route,movement_file,shops_file):
    motion=[r for line in movement_file.read_text(encoding='utf-8').splitlines()
            if (r:=json.loads(line))['type']=='sample']
    shops=[r for line in shops_file.read_text(encoding='utf-8').splitlines()
           if (r:=json.loads(line))['type']=='shop']
    line=LineString([r['position'][:2] for r in motion]) if len(motion)>1 else None
    results=[]
    for target in route['targets']:
        shop=next((s for s in shops if s['trader_key']==target['trader_key']),None)
        row={'name':target['name'],'trader_key':target['trader_key'],'corner':target['corner'],
             'read':shop is not None,'planned_min_distance':target['planned_min_distance'],
             'observed_min_distance':line.distance(Point(target['x'],target['y'])) if line else None}
        if shop:
            when=datetime.fromisoformat(shop['at'])
            after=[p for p in motion if .8<=(datetime.fromisoformat(p['at'])-when).total_seconds()<=1.4]
            row.update(rows=shop['row_count'],precision=shop['precision'],
                       read_distance=shop['actions'][-1]['distance'],
                       server_distance=shop['actions'][-1]['server_distance'],
                       moved_on_after_read=max((math.dist(shop['actions'][-1]['position'][:2],p['position'][:2])
                                                for p in after),default=None))
        results.append(row)
    return results
