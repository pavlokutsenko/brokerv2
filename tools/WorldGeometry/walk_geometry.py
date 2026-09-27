"""Offline conservative section geometry, bounded grid routing and curve fitting."""
import heapq
import math
from shapely.geometry import Point, LineString, Polygon, box
from shapely.ops import unary_union


def polygon_rings(rings,origin=(80000,147000)):
    result=Polygon()
    for ring in rings:
        poly=Polygon([(x+origin[0],y+origin[1]) for x,y in ring]).buffer(0)
        if not poly.is_empty:
            result=result.symmetric_difference(poly)
    return result


class Navigation:
    def __init__(self,data,clearance=55,cell=40):
        self.cell=cell
        e=data["extent"]
        self.origin=data.get('origin',(80000,147000))
        self.extent=[e[0]+self.origin[0],e[1]+self.origin[0],e[2]+self.origin[1],e[3]+self.origin[1]]
        self.region=box(self.extent[0]+35,self.extent[2]+35,self.extent[1]-35,self.extent[3]-35)
        obstacles=[polygon_rings(o["rings"],self.origin) for o in data["obstacles"]+data["unknown"]]
        if data.get('ground'):
            from ground_surface import cliff_edges
            obstacles.append(cliff_edges(data['ground']))
        # Confirmed traps survive relaxed room projections and ordinary probes.
        # Bounds are absolute city coordinates, with an explicit survey margin.
        self.forbidden=unary_union([box(z['bounds'][0],z['bounds'][2],z['bounds'][1],z['bounds'][3])
                                   for z in data.get('hardExclusions',[])])
        self.blocked=unary_union(obstacles).buffer(clearance).union(self.forbidden.buffer(clearance))
        self.free=self.region.difference(self.blocked)
        self.nodes={}
        for ix in range(math.ceil((self.extent[1]-self.extent[0])/cell)+1):
            for iy in range(math.ceil((self.extent[3]-self.extent[2])/cell)+1):
                p=(self.extent[0]+ix*cell,self.extent[2]+iy*cell)
                if self.free.covers(Point(p)):
                    self.nodes[ix,iy]=p
        self.edges={}

    def fork(self):
        """Reuse surveyed geometry/grid, isolating per-approach learned blockers."""
        result=object.__new__(Navigation)
        result.__dict__=self.__dict__.copy()
        result.nodes=self.nodes.copy()
        result.edges=self.edges.copy()
        return result

    def add_blocker(self, geometry):
        """Add an already inflated observed obstacle and invalidate cached edges."""
        self.blocked=self.blocked.union(geometry)
        self.free=self.region.difference(self.blocked)
        self.nodes={key:p for key,p in self.nodes.items() if self.free.covers(Point(p))}
        self.edges.clear()

    def clear(self,a,b):
        return self.free.covers(LineString([a,b])) if a!=b else self.free.covers(Point(a))

    def clear_forbidden(self,a,b):
        segment=LineString([a,b]) if a!=b else Point(a)
        return not self.forbidden.intersects(segment)

    def anchor(self,p):
        key=(round((p[0]-self.extent[0])/self.cell),round((p[1]-self.extent[2])/self.cell))
        choices=[k for k in self.nodes if abs(k[0]-key[0])<=3 and abs(k[1]-key[1])<=3]
        for k in sorted(choices,key=lambda k:math.dist(p,self.nodes[k])):
            if self.clear(p,self.nodes[k]):
                return k
        raise RuntimeError("point has no safe grid connection")

    def shortest(self,a,b):
        a,b=tuple(a),tuple(b)
        if self.clear(a,b):
            return [a,b]
        start,end=self.anchor(a),self.anchor(b)
        queue=[(0,start)]
        cost={start:0};parents={}
        closed=set()
        while queue:
            _,cur=heapq.heappop(queue)
            if cur in closed:
                continue
            closed.add(cur)
            if cur==end:
                keys=[cur]
                while cur!=start:
                    cur=parents[cur];keys.append(cur)
                raw=[a,*[self.nodes[k] for k in reversed(keys)],b]
                simplified=[raw[0]]
                i=0
                while i<len(raw)-1:
                    j=len(raw)-1
                    while j>i+1 and not self.clear(raw[i],raw[j]):
                        j-=1
                    simplified.append(raw[j]);i=j
                return simplified
            for dx,dy in ((1,0),(-1,0),(0,1),(0,-1),(1,1),(1,-1),(-1,1),(-1,-1)):
                nxt=cur[0]+dx,cur[1]+dy
                if nxt not in self.nodes or nxt in closed:
                    continue
                edge=tuple(sorted((cur,nxt)))
                if edge not in self.edges:
                    self.edges[edge]=self.clear(self.nodes[cur],self.nodes[nxt])
                if not self.edges[edge]:
                    continue
                value=cost[cur]+self.cell*math.hypot(dx,dy)
                if value<cost.get(nxt,float('inf')):
                    cost[nxt]=value;parents[nxt]=cur
                    heapq.heappush(queue,(value+math.dist(self.nodes[nxt],self.nodes[end]),nxt))
        raise RuntimeError("no route in decoded geometry")


def rounded_route(raw,nav):
    clean=[tuple(raw[0])]
    for p in raw[1:]:
        if math.dist(clean[-1],p)>1:
            clean.append(tuple(p))
    result=[clean[0]]
    for a,b,c in zip(clean,clean[1:],clean[2:]):
        cut=min(115,.44*math.dist(a,b),.44*math.dist(b,c))
        accepted=None
        while cut>=5:
            entry=tuple(b[i]+(a[i]-b[i])*cut/math.dist(a,b) for i in (0,1))
            leave=tuple(b[i]+(c[i]-b[i])*cut/math.dist(c,b) for i in (0,1))
            curve=[tuple((1-t)**2*entry[i]+2*(1-t)*t*b[i]+t*t*leave[i] for i in (0,1)) for t in [j/12 for j in range(13)]]
            if all(nav.clear(p,q) for p,q in zip([result[-1],*curve],curve)):
                accepted=curve;break
            cut*=.7
        result.extend(accepted if accepted else [b])
    result.append(clean[-1])
    dense=[result[0]]
    for a,b in zip(result,result[1:]):
        n=max(1,math.ceil(math.dist(a,b)/22))
        dense.extend(tuple(a[k]+(b[k]-a[k])*j/n for k in (0,1)) for j in range(1,n+1))
    if not all(nav.clear(a,b) for a,b in zip(dense,dense[1:])):
        raise RuntimeError("smoothed route crosses inflated obstacles")
    return dense
