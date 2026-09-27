"""Validated, city-scoped navigation data shared by source and packaged worker."""
import json
import math
from shapely.geometry import Polygon
from pathlib import Path


def map_root():
    packaged = Path(__file__).resolve().parent / 'maps'
    return packaged if (packaged / 'cities.json').exists() else Path(__file__).resolve().parents[2] / 'maps'


def load_navigation(city, root=None):
    root = Path(root or map_root()).resolve()
    registry = json.loads((root / 'cities.json').read_text(encoding='utf-8-sig'))
    if city not in registry:
        raise RuntimeError(f'No validated navigation map for city {city}')
    descriptor = (root / registry[city]).resolve()
    if not descriptor.is_relative_to(root):
        raise RuntimeError('City descriptor escapes map directory')
    profile = json.loads(descriptor.read_text(encoding='utf-8-sig'))
    if profile.get('schema') != 1 or profile.get('city') != city or profile.get('validatedCapsule') != [9, 23]:
        raise RuntimeError('City navigation validation profile is incompatible')
    source = (descriptor.parent / profile['navigation']).resolve()
    if not source.is_relative_to(descriptor.parent):
        raise RuntimeError('Navigation map escapes city directory')
    data = json.loads(source.read_text(encoding='utf-8-sig'))
    origin=profile.get('coordinateOrigin',[80000,147000])
    if len(origin)!=2 or not all(isinstance(v,(int,float)) and math.isfinite(v) and abs(v)<1e7 for v in origin):
        raise RuntimeError('Invalid city coordinate origin')
    exclusions=profile.get('hardExclusions',[])
    for zone in exclusions:
        bounds=zone.get('bounds',[])
        if (len(bounds)!=4 or not all(isinstance(v,(int,float)) and math.isfinite(v) and abs(v)<1e7 for v in bounds)
                or bounds[0]>=bounds[1] or bounds[2]>=bounds[3]):
            raise RuntimeError('Invalid city hard exclusion bounds')
    zone=profile.get('collectionZone')
    if zone is not None:
        points=zone.get('polygon',[])
        if not 3<=len(points)<=64 or not all(len(p)==2 and all(isinstance(v,(int,float)) and math.isfinite(v) for v in p) for p in points):
            raise RuntimeError('Invalid city collection zone coordinates')
        polygon=Polygon(points)
        if not polygon.is_valid or polygon.area<=0:
            raise RuntimeError('Invalid city collection zone polygon')
    return {**data, 'city': city, 'origin':origin, 'navigationRules': profile.get('navigationRules', {}),
            'hardExclusions':exclusions,'collectionZone':zone}
