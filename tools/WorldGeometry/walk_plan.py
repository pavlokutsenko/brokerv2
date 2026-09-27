"""Coverage route near captured traders, avoiding known/unknown map obstacles."""
import argparse
import json
import math
from pathlib import Path
import numpy as np
from shapely.geometry import Point
from walk_geometry import Navigation,rounded_route


def nearby_segments(points,path,radius):
    hit=np.zeros(len(points),dtype=bool)
    for a,b in zip(path,path[1:]):
        a,b=np.asarray(a),np.asarray(b);d=b-a
        t=np.clip(((points-a)@d)/max(float(d@d),1e-9),0,1)
        hit |= np.sum((points-a-t[:,None]*d)**2,axis=1)<=radius*radius
    return hit


def plan(data,start,radius=125,goal=.97):
    nav=Navigation(data)
    if not nav.free.covers(Point(start)):
        raise RuntimeError("start lies inside unknown/obstacle clearance; move to open ground")
    traders=np.array([[t[1]+80000,t[2]+147000] for t in data["traders"]])
    candidates=[]
    for x in np.arange(nav.extent[0]+80,nav.extent[1]-60,155):
        for y in np.arange(nav.extent[2]+80,nav.extent[3]-60,155):
            if not nav.free.covers(Point(x,y)):
                continue
            cover=np.sum((traders-[x,y])**2,axis=1)<=radius**2
            if cover.any():
                candidates.append(((float(x),float(y)),cover))
    covered=np.sum((traders-start)**2,axis=1)<=radius**2
    route=[tuple(start)];visits=[];unreachable=[]
    for _ in range(180):
        if covered.mean()>=goal:
            break
        current=route[-1]
        choices=[]
        for index,(point,cover) in enumerate(candidates):
            gain=int((cover&~covered).sum())
            if not gain:
                continue
            distance=math.dist(current,point)
            turn=0
            if len(route)>1:
                a=np.array(current)-route[-2];b=np.array(point)-current
                turn=math.acos(float(np.clip(a@b/max(np.linalg.norm(a)*np.linalg.norm(b),1e-9),-1,1)))
            choices.append(((distance+80*turn)/(gain**.45),index))
        if not choices:
            break
        accepted=None
        for _,index in sorted(choices)[:12]:
            point,_=candidates[index]
            try:
                segment=nav.shortest(current,point)
            except RuntimeError:
                unreachable.append(point);candidates[index]=(point,np.zeros(len(traders),dtype=bool));continue
            length=sum(math.dist(a,b) for a,b in zip(segment,segment[1:]))
            gain=int((nearby_segments(traders,segment,radius)&~covered).sum())
            score=length/max(gain,1)**.45
            if accepted is None or score<accepted[0]:
                accepted=score,segment,index
        if accepted is None:
            continue
        _,segment,index=accepted
        route.extend(segment[1:]);visits.append(segment[-1])
        covered|=nearby_segments(traders,segment,radius)
        candidates[index]=(candidates[index][0],np.zeros(len(traders),dtype=bool))
    return_segment=nav.shortest(route[-1],start)
    route.extend(return_segment[1:])
    smooth=rounded_route(route,nav)
    coverage=nearby_segments(traders,smooth,radius)
    return {"source_capture":data["captured"],"start":start,"radius":radius,"clearance":55,
            "points":[[round(v,3) for v in p] for p in smooth],"control_points":route,
            "visits":len(visits),"length":sum(math.dist(a,b) for a,b in zip(smooth,smooth[1:])),
            "traders":len(traders),"planned_nearby":int(coverage.sum()),
            "unreachable_candidates":unreachable,"note":"Planning against partial approximate sections; live walkability is experimental."}


if __name__ == "__main__":
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("map",type=Path)
    p.add_argument("output",type=Path)
    p.add_argument("--start",nargs=2,type=float)
    a=p.parse_args()
    data=json.loads(a.map.read_text(encoding="utf-8"))
    result=plan(data,a.start or [data['player'][0]+80000,data['player'][1]+147000])
    a.output.write_text(json.dumps(result,indent=2),encoding="utf-8")
    print(json.dumps({k:v for k,v in result.items() if k not in ('points','control_points','unreachable_candidates')}))
