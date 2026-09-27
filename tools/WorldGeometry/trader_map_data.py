"""Small display-only snapshot of city geometry and server trader coordinates."""
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
from city_maps import load_navigation
from walk_geometry import polygon_rings
from collection_zone import in_collection_zone


def snapshot(city, traders, center):
    data=load_navigation(city);origin=data['origin']
    zone=data.get('collectionZone')
    for trader in traders:
        trader['insideZone']=in_collection_zone(zone,trader['x'],trader['y'])
    shapes=[]
    for obstacle in data['obstacles']:
        # Display the original decoded sections; duplicate capsule bands hide
        # narrow passages. This drawing does not determine route reachability.
        if obstacle.get('name','').endswith(' [capsule band]'): continue
        geom=polygon_rings(obstacle['rings'],origin).simplify(2,preserve_topology=True)
        for poly in ([geom] if geom.geom_type=='Polygon' else getattr(geom,'geoms',[])):
            if poly.is_empty: continue
            shapes.append({'name':obstacle.get('name',''), 'rings':[
                [[round(x,1),round(y,1)] for x,y in ring.coords]
                for ring in [poly.exterior,*poly.interiors]]})
    return {'city':city,'at':datetime.now(timezone.utc).isoformat(),'center':center,
            'extent':[data['extent'][0]+origin[0],data['extent'][1]+origin[0],
                      data['extent'][2]+origin[1],data['extent'][3]+origin[1]],
            'shapes':shapes,'exclusions':data['hardExclusions'],'traders':traders,
            'collectionZone':zone}


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--city',required=True)
    p.add_argument('--traders',type=Path,required=True)
    p.add_argument('--center',nargs=2,type=float,required=True)
    p.add_argument('--output',type=Path,required=True)
    a=p.parse_args()
    a.output.write_text(json.dumps(snapshot(a.city,json.loads(a.traders.read_text(encoding='utf-8-sig')),
                                            a.center),ensure_ascii=False,separators=(',',':')),encoding='utf-8')
