"""Export a saved market walk and obstacle proximity evidence without a client."""
import argparse
import json
from pathlib import Path
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from shapely.geometry import LineString
from walk_geometry import polygon_rings
from walk_plan import nearby_segments


def report(map_file,log_files,prefix,shops_file=None,plan_file=None):
    data=json.loads(map_file.read_text(encoding='utf-8'));routes=[];events=[]
    for name in log_files:
        rows=[json.loads(line) for line in name.read_text(encoding='utf-8').splitlines()]
        routes.append([r['position'][:2] for r in rows if r['type']=='sample'])
        events.extend(r for r in rows if r['type']=='recovery')
    traders=np.array([[t[1]+80000,t[2]+147000] for t in data['traders']])
    covered=np.zeros(len(traders),dtype=bool)
    for route in routes: covered|=nearby_segments(traders,route,125)
    lines=[LineString(r) for r in routes if len(r)>1]
    proximity={}
    fig,ax=plt.subplots(figsize=(15,11),dpi=160)
    fig.patch.set_facecolor('#111827');ax.set_facecolor('#111827')
    for item in data['obstacles']+data['unknown']:
        shape=polygon_rings(item['rings'])
        key=item.get('component',item['name'])
        if lines:
            distance=min(line.distance(shape) for line in lines)
            if key not in proximity or distance<proximity[key]['distance']:
                proximity[key]={'name':item['name'],'mesh':item.get('mesh'),'distance':round(distance,2)}
        for polygon in getattr(shape,'geoms',[shape]):
            if polygon.geom_type!='Polygon': continue
            x,y=polygon.exterior.xy
            ax.fill(np.asarray(x)-80000,np.asarray(y)-147000,color='#667085',alpha=.60)
            for hole in polygon.interiors:
                x,y=hole.xy;ax.fill(np.asarray(x)-80000,np.asarray(y)-147000,color='#111827')
    for mask,color,label in ((~covered,'#ef927f','Дальше 125 единиц от пути'),(covered,'#7fcdbb','Прошли в пределах 125 единиц')):
        ax.scatter(traders[mask,0]-80000,traders[mask,1]-147000,s=4,c=color,label=label,zorder=3)
    for index,route in enumerate(routes):
        if not route: continue
        xy=np.asarray(route)-[80000,147000]
        ax.plot(xy[:,0],xy[:,1],lw=1.5,c='#facc15',label='Фактический путь' if index==0 else None,zorder=4)
        ax.scatter(*xy[0],s=70,marker='o',c='#22c55e',zorder=6)
        ax.scatter(*xy[-1],s=80,marker='x',c='white',zorder=6)
    for index,event in enumerate(events):
        x,y=event['position'];ax.scatter(x-80000,y-147000,s=70,marker='D',c='#fb7185',zorder=7)
        ax.annotate(str(index+1),(x-80000,y-147000),xytext=(5,6),textcoords='offset points',color='white')
    shops=[]
    if shops_file:
        shops=[r for line in shops_file.read_text(encoding='utf-8').splitlines()
               if (r:=json.loads(line)).get('type')=='shop']
        if shops:
            xy=np.array([[s['trader']['x']-80000,s['trader']['y']-147000] for s in shops])
            ax.scatter(xy[:,0],xy[:,1],s=12,facecolors='none',edgecolors='#ff89e5',linewidths=.6,
                       label=f'Прочитано лавок на ходу: {len(shops)}',zorder=5)
    if plan_file:
        plan=json.loads(plan_file.read_text(encoding='utf-8'))
        for index,target in enumerate(plan.get('targets',[])):
            x,y=target['x']-80000,target['y']-147000
            ax.add_patch(plt.Circle((x,y),plan.get('standoff',45),fill=False,color='#fb923c',lw=1.2,zorder=7))
            ax.annotate(target['name'],(x,y),xytext=(9,-16 if index%2 else 12),textcoords='offset points',
                        color='white',fontsize=9,bbox={'facecolor':'#111827','alpha':.9,'edgecolor':'none'},zorder=8)
    ax.set_aspect('equal');ax.invert_yaxis();ax.tick_params(colors='#d1d5db')
    ax.set_xlabel('Мировой X − 80000',color='#d1d5db');ax.set_ylabel('Мировой Y − 147000',color='#d1d5db')
    ax.set_title(f'Гиран · {covered.sum()} / {len(traders)} трейдеров снимка · {len(events)} локальных обходов\n'
                 'Зелёный круг — старт · белый крест — финиш · ромбы — перепланирование',color='white',pad=16,fontsize=12)
    ax.legend(loc='upper left',facecolor='#1f2937',labelcolor='white',fontsize=9)
    fig.tight_layout();fig.savefig(prefix.with_suffix('.png'));plt.close(fig)
    result={'logs':[str(p) for p in log_files],'nearby':int(covered.sum()),'traders':len(traders),
            'nearby_percent':round(100*covered.mean(),2),'recoveries':len(events),
            'components_within_150':sum(v['distance']<=150 for v in proximity.values()),
            'proximity':sorted(proximity.values(),key=lambda v:v['distance']),
            'note':'Proximity to captured geometry is not proof of testing every side or every live obstacle.'}
    if shops_file:
        result['shops']={'log':str(shops_file),'captured':len(shops),'rows':sum(s['row_count'] for s in shops),
                         'wire_int64':sum(s['precision']=='wire_int64' for s in shops)}
    prefix.with_suffix('.json').write_text(json.dumps(result,indent=2,ensure_ascii=False),encoding='utf-8')
    print(json.dumps({k:v for k,v in result.items() if k!='proximity'}))


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('map',type=Path);p.add_argument('output_prefix',type=Path);p.add_argument('logs',nargs='+',type=Path)
    p.add_argument('--shops',type=Path)
    p.add_argument('--plan',type=Path)
    a=p.parse_args();report(a.map,a.logs,a.output_prefix,a.shops,a.plan)
