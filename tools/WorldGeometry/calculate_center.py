"""Calculate a radar-safe standing disk from the whole approved polygon."""
import argparse
import itertools
import json
import math
from pathlib import Path


def calculate_center(polygon, standing_radius=200, radar_radius=3000, margin=100):
    points=[tuple(p) for p in polygon]
    if len(points)<3 or any(len(p)!=2 or not all(math.isfinite(x) for x in p) for p in points):
        raise ValueError('A finite collection polygon is required')
    # Translate first to keep circumcenter arithmetic accurate at world coordinates.
    origin=points[0];local=[(x-origin[0],y-origin[1]) for x,y in points]
    candidates=list(local)
    for a,b in itertools.combinations(local,2):
        candidates.append(((a[0]+b[0])/2,(a[1]+b[1])/2))
    for a,b,c in itertools.combinations(local,3):
        ax,ay=a;bx,by=b;cx,cy=c
        divisor=2*(ax*(by-cy)+bx*(cy-ay)+cx*(ay-by))
        if abs(divisor)<1e-9:continue
        aa=ax*ax+ay*ay;bb=bx*bx+by*by;cc=cx*cx+cy*cy
        candidates.append(((aa*(by-cy)+bb*(cy-ay)+cc*(ay-by))/divisor,
            (aa*(cx-bx)+bb*(ax-cx)+cc*(bx-ax))/divisor))
    best=min(candidates,key=lambda c:max(math.dist(c,p) for p in local))
    cover=max(math.dist(best,p) for p in local)
    return dict(center=[best[0]+origin[0],best[1]+origin[1]],standingRadius=standing_radius,
        coveringRadius=cover,worstDistance=cover+standing_radius,
        remainingRange=radar_radius-cover-standing_radius,
        coversZone=cover+standing_radius<=radar_radius-margin)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('descriptor',type=Path)
    parser.add_argument('--standing-radius',type=float,default=200)
    args=parser.parse_args()
    descriptor=json.loads(args.descriptor.read_text(encoding='utf-8-sig'))
    print(json.dumps(calculate_center(descriptor['collectionZone']['polygon'],args.standing_radius),indent=2))
