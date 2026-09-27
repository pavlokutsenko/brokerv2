"""Ground height from captured floor triangles, with an exportable sampled grid."""
import math
import numpy as np
from shapely.geometry import LineString
from shapely.ops import unary_union
from triangle_geometry import transform, components


class GroundSurface:
    def __init__(self, raw):
        self.bins = {}
        self.faces = []
        for item in raw['instances']:
            name = item['mesh']['name']
            if item['geometry_error'] or not any(k in name for k in ('Floor', 'Stair', 'Elevation')):
                continue
            asset = raw['assets'][item['mesh']['address']]
            vertices = np.asarray(transform(asset['vertices'], **item['transform']))
            ignored = set()
            if name == 'Giran_V_Plaza_Stair01':
                for group in components(asset['vertices'], asset['triangles']):
                    if len(group['triangle_indices']) in (26, 70):
                        ignored.update(group['triangle_indices'])
            for index, indices in enumerate(asset['triangles']):
                if index in ignored:
                    continue
                face = vertices[list(indices)]
                normal = np.cross(face[1]-face[0], face[2]-face[0])
                if abs(normal[2]) < .65*np.linalg.norm(normal) or abs(normal[2]) < .01:
                    continue
                if min(face[:, 2]) < -3570 or max(face[:, 2]) > -3300:
                    continue
                ident = len(self.faces)
                self.faces.append(face)
                lower = np.floor(face[:, :2].min(axis=0)/100).astype(int)
                upper = np.floor(face[:, :2].max(axis=0)/100).astype(int)
                if np.prod(upper-lower+1) > 20000:
                    raise RuntimeError('floor triangle bin budget exceeded')
                for x in range(lower[0], upper[0]+1):
                    for y in range(lower[1], upper[1]+1):
                        self.bins.setdefault((x,y), []).append(ident)
        self.faces = np.asarray(self.faces)

    def height(self, x, y):
        ids = self.bins.get((math.floor(x/100),math.floor(y/100)), [])
        if not ids:
            return None
        f = self.faces[ids]
        a, b, c = f[:,0], f[:,1], f[:,2]
        denominator = (b[:,1]-c[:,1])*(a[:,0]-c[:,0])+(c[:,0]-b[:,0])*(a[:,1]-c[:,1])
        u = ((b[:,1]-c[:,1])*(x-c[:,0])+(c[:,0]-b[:,0])*(y-c[:,1]))/denominator
        v = ((c[:,1]-a[:,1])*(x-c[:,0])+(a[:,0]-c[:,0])*(y-c[:,1]))/denominator
        mask = (u>=-1e-5)&(v>=-1e-5)&(u+v<=1.00001)
        heights = u*a[:,2]+v*b[:,2]+(1-u-v)*c[:,2]
        return float(heights[mask].max()) if mask.any() else None

    def grid(self, extent, step=20):
        xmin,xmax,ymin,ymax=extent
        nx,ny=math.ceil((xmax-xmin)/step)+1,math.ceil((ymax-ymin)/step)+1
        values=[]
        for iy in range(ny):
            for ix in range(nx):
                z=self.height(xmin+ix*step,ymin+iy*step)
                values.append(round(z,2) if z is not None else None)
        return {'origin':[xmin,ymin],'step':step,'nx':nx,'ny':ny,'values':values}


class GroundGrid:
    def __init__(self,data):
        self.data=data

    def height(self,x,y):
        if not self.data:
            return None
        g=self.data
        fx=(x-g['origin'][0])/g['step'];fy=(y-g['origin'][1])/g['step']
        ix,iy=math.floor(fx),math.floor(fy)
        if not (0<=ix<g['nx']-1 and 0<=iy<g['ny']-1):
            return None
        values=[g['values'][(iy+dy)*g['nx']+ix+dx] for dx,dy in ((0,0),(1,0),(0,1),(1,1))]
        if any(v is None for v in values):
            valid=[v for v in values if v is not None]
            return max(valid) if valid else None
        tx,ty=fx-ix,fy-iy
        return values[0]*(1-tx)*(1-ty)+values[1]*tx*(1-ty)+values[2]*(1-tx)*ty+values[3]*tx*ty


def cliff_edges(grid,threshold=24):
    """Separate adjacent floor samples with an unwalkable height discontinuity."""
    g=grid;marks=[];step=g['step']
    for iy in range(g['ny']):
        for ix in range(g['nx']):
            a=g['values'][iy*g['nx']+ix]
            if a is None: continue
            x=g['origin'][0]+ix*step;y=g['origin'][1]+iy*step
            for dx,dy in ((1,0),(0,1)):
                if ix+dx>=g['nx'] or iy+dy>=g['ny']: continue
                b=g['values'][(iy+dy)*g['nx']+ix+dx]
                if b is None or abs(a-b)<=threshold: continue
                cx=x+dx*step/2;cy=y+dy*step/2
                marks.append(LineString([(cx-dy*step/2,cy-dx*step/2),(cx+dy*step/2,cy+dx*step/2)]))
    return unary_union(marks).buffer(1)
