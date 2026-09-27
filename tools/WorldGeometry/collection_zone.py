"""City price collection boundary, independent of obstacle clearance."""
import math


def in_collection_zone(zone,x,y):
    if zone is None: return True
    if not isinstance(x,(int,float)) or not isinstance(y,(int,float)) or not math.isfinite(x) or not math.isfinite(y):
        return False
    polygon=zone['polygon'];inside=False
    for a,b in zip(polygon,polygon[1:]+polygon[:1]):
        ax,ay=a;bx,by=b
        cross=(x-ax)*(by-ay)-(y-ay)*(bx-ax)
        if abs(cross)<1e-7 and min(ax,bx)<=x<=max(ax,bx) and min(ay,by)<=y<=max(ay,by): return True
        if (ay>y)!=(by>y) and x<(bx-ax)*(y-ay)/(by-ay)+ax: inside=not inside
    return inside
