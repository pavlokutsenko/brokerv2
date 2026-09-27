"""Deliberate loops around each stair partition, the monument and four lamps."""
import math
import numpy as np
from shapely.ops import unary_union
from shapely.geometry import Point
from walk_geometry import Navigation,polygon_rings,rounded_route
from walk_plan import nearby_segments


def course(data,start):
    nav=Navigation(data);objects=[]
    for mesh in ('Giran_V_Plaza_Stair01','Giran_AdenaGirl_Btm','O_LoadLamp01'):
        union=unary_union([polygon_rings(o['rings']) for o in data['obstacles'] if o['mesh']==mesh])
        parts=getattr(union,'geoms',[union])
        for index,poly in enumerate(sorted(parts,key=lambda p:p.bounds[1])):
            if poly.area>3:
                objects.append((f'{mesh} #{index+1}',poly))
    # The four small lamps are mounted inside the monument's footprint. Their
    # backs cannot be circled independently; test the enclosing obstacle once.
    monuments=[poly for name,poly in objects if name.startswith('Giran_AdenaGirl_Btm')]
    attached=[name for name,poly in objects if name.startswith('O_LoadLamp01')
              and any(parent.envelope.covers(poly.centroid) for parent in monuments)]
    objects=[(name,poly) for name,poly in objects if name not in attached]
    raw=[tuple(start)];landmarks=[];skipped=[]
    while objects:
        index=min(range(len(objects)),key=lambda i:objects[i][1].distance(Point(raw[-1])))
        name,poly=objects.pop(index);xmin,ymin,xmax,ymax=poly.bounds
        corners=[(xmin-85,ymin-85),(xmax+85,ymin-85),(xmax+85,ymax+85),(xmin-85,ymax+85)]
        adjusted=[]
        for point in corners:
            if nav.clear(point,point):
                adjusted.append(point);continue
            candidates=sorted(nav.nodes.values(),key=lambda p:math.dist(point,p))
            if math.dist(point,candidates[0])>100:
                break
            adjusted.append(candidates[0])
        if len(adjusted)!=4:
            skipped.append(name);continue
        first=min(range(4),key=lambda i:math.dist(raw[-1],adjusted[i]))
        adjusted=adjusted[first:]+adjusted[:first]
        candidate=[raw[-1]]
        try:
            for point in [*adjusted,adjusted[0]]:
                candidate.extend(nav.shortest(candidate[-1],point)[1:])
        except RuntimeError:
            skipped.append(name);continue
        raw.extend(candidate[1:]);landmarks.append({'name':name,'corners':adjusted,'bounds':poly.bounds,
            'includes':attached if name.startswith('Giran_AdenaGirl_Btm') else []})
    raw.extend(nav.shortest(raw[-1],start)[1:])
    points=rounded_route(raw,nav)
    traders=np.array([[t[1]+80000,t[2]+147000] for t in data['traders']])
    return {'source_capture':data['captured'],'start':start,'radius':125,'clearance':55,
            'points':points,'control_points':raw,'visits':len(landmarks),'landmarks':landmarks,
            'skipped_landmarks':skipped,'length':sum(math.dist(a,b) for a,b in zip(points,points[1:])),
            'traders':len(traders),'planned_nearby':int(nearby_segments(traders,points,125).sum())}
