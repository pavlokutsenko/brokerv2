"""Augment the saved drawing with obstacle surfaces in the local capsule band."""
import argparse
import hashlib
import json
from pathlib import Path
import numpy as np
from shapely.geometry import Polygon, LineString, box
from shapely.ops import unary_union
from triangle_geometry import transform, components
from ground_surface import GroundSurface
from project_market import rings


def clip_z(poly,z,above):
    result=[]
    for a,b in zip(poly,poly[1:]+poly[:1]):
        ina,inb=(a[2]>=z,b[2]>=z) if above else (a[2]<=z,b[2]<=z)
        if ina: result.append(a)
        if ina!=inb:
            t=(z-a[2])/(b[2]-a[2])
            result.append([a[i]+t*(b[i]-a[i]) for i in range(3)])
    return result


def pieces(face):
    pending=[(face,0)]
    while pending:
        tri,depth=pending.pop()
        sizes=[np.linalg.norm(tri[i][:2]-tri[(i+1)%3][:2]) for i in range(3)]
        i=int(np.argmax(sizes))
        if max(sizes)>110 and depth<8:
            a,b,c=tri[i],tri[(i+1)%3],tri[(i+2)%3];mid=(a+b)/2
            pending.extend([(np.array([a,mid,c]),depth+1),(np.array([mid,b,c]),depth+1)])
        else:
            yield tri


def build(raw,original):
    e=original['extent'];extent=[e[0]+80000,e[1]+80000,e[2]+147000,e[3]+147000]
    crop=box(extent[0],extent[2],extent[1],extent[3])
    ground=GroundSurface(raw);extra=[]
    for item in raw['instances']:
        name=item['mesh']['name']
        if item['geometry_error'] or item['collision_enabled'].endswith('NoCollision'):
            continue
        if 'Floor' in name:
            continue
        asset=raw['assets'][item['mesh']['address']]
        verts=np.asarray(transform(asset['vertices'],**item['transform']))
        faces=asset['triangles']
        if name=='Giran_V_Plaza_Stair01':
            keep=[i for group in components(asset['vertices'],faces) if len(group['triangle_indices'])==70 for i in group['triangle_indices']]
            faces=[faces[i] for i in keep]
        marks=[]
        for indices in faces:
            face=verts[list(indices)]
            if 'Elevation' in name:
                normal=np.cross(face[1]-face[0],face[2]-face[0])
                if abs(normal[2])>.5*np.linalg.norm(normal):
                    continue
            if min(face[:,2])>-3260 or max(face[:,2])<-3570:
                continue
            for part in pieces(face):
                centre=part.mean(axis=0)
                if not (extent[0]-100<=centre[0]<=extent[1]+100 and extent[2]-100<=centre[1]<=extent[3]+100):
                    continue
                floor=ground.height(*centre[:2])
                if floor is None:
                    continue
                clipped=clip_z(clip_z(part.tolist(),floor+4,True),floor+50,False)
                if len(clipped)<2: continue
                xy=[p[:2] for p in clipped]
                poly=Polygon(xy).buffer(0) if len(xy)>=3 else Polygon()
                marks.append(poly if poly.area>.1 else LineString(xy).buffer(.65))
        if marks:
            contours=rings(unary_union(marks),crop)
            if contours:
                extra.append({'name':item['actor']['name']+' [capsule band]','mesh':name,
                              'component':item['component']['address'],'rings':contours,'triangles':len(faces)})
    return {**original,'version':2,'obstacles':original['obstacles']+extra,'ground':ground.grid(extent),
            'navigation_note':'Original sections plus decoded surfaces 4..50 units above local floor; native query/recovery required.',
            'capsule_band_objects':len(extra)}


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('scene',type=Path);p.add_argument('map',type=Path);p.add_argument('output',type=Path)
    a=p.parse_args();raw=a.scene.read_bytes()
    data=build(json.loads(raw),json.loads(a.map.read_text(encoding='utf-8')))
    data['source_scene_sha256']=hashlib.sha256(raw).hexdigest()
    a.output.write_text(json.dumps(data,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    print(json.dumps({'objects':len(data['obstacles']),'capsule_band_objects':data['capsule_band_objects'],
                      'ground_cells':len(data['ground']['values']),'missing_ground':sum(z is None for z in data['ground']['values']),
                      'bytes':a.output.stat().st_size}))
