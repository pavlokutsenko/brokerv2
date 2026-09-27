"""Previous-session positions nominate approaches, never game actions."""
import math
import time
from walk_shop_policy import trader_key
from walk_shop_lifecycle import close_visit
from collection_zone import in_collection_zone


def session_targets(shops):
    return [*shops.tracked_targets,*shops.dynamic_targets.values()]


def needs_unbound_scan(shops,position):
    return any(t.get('rebind') and t['object_id']==0 and
               math.dist(position[:2],(t['x'],t['y']))<=110 for t in session_targets(shops))


def continuation_nominations(shops,payload,log):
    if payload.get('pid')!=shops.walk.pid:
        return
    for row in payload.get('traders',[]):
        key=trader_key(row['name'])
        if (int(row['object_id'])!=0 or int(row['kiosk_type']) not in (1,3,8) or not key
                or key in shops.dynamic_targets or key in shops.captured_keys or key in shops.unavailable_keys
                or not all(math.isfinite(row[k]) for k in ('x','y'))
                or not in_collection_zone(shops.collection_zone,row['x'],row['y'])):
            continue
        target={**row,'key':key,'rebind':True,'continuation_nomination':True,'seen_at':time.monotonic()}
        shops.dynamic_targets[key]=target;shops.dynamic_seen_live[key]=target
        shops.allowed_keys.add(key)
        log({'type':'continuation_approach_nominated','key':key,'x':row['x'],'y':row['y']})


def bind_native_session_targets(shops,memory,actors,position,log):
    for target in session_targets(shops):
        if not target.get('rebind') or target['object_id']!=0 or math.dist(position[:2],(target['x'],target['y']))>110:
            continue
        actor=next((a for a in actors if trader_key(a['name'])==target['key'] and a['object_id']>0 and
                    math.dist((a['x'],a['y']),(target['x'],target['y']))<20),None)
        if actor is None:
            shops.unbound_absence_scans[target['key']]=shops.unbound_absence_scans.get(target['key'],0)+1
            shops.unbound_scan_at=time.monotonic()
            continue
        fresh=shops.fresh(memory,actor)
        if (fresh is None or trader_key(fresh['name'])!=target['key'] or fresh['object_id']!=actor['object_id']
                or math.dist((fresh['x'],fresh['y']),(target['x'],target['y']))>=20
                or fresh['kiosk_type'] not in (0,1,3,8)):
            continue
        target['object_id']=fresh['object_id'];shops.allowed_object_ids.add(fresh['object_id'])
        shops.unbound_absence_scans.pop(target['key'],None)
        log({'type':'runtime_native_rebound','key':target['key'],'object_id':fresh['object_id']})
        if fresh['kiosk_type']!=target['kiosk_type']:
            close_visit(shops,target['key'],fresh['object_id'],log,
                        'native kiosk=0' if fresh['kiosk_type']==0 else 'native shop type changed')


def session_candidate_matches(shops,trader):
    key=trader_key(trader['name'])
    target=next((t for t in session_targets(shops) if t['key']==key and t.get('rebind')),None)
    return (target is None or target['object_id']>0 and target['object_id']==trader['object_id']
            and target['kiosk_type']==trader['kiosk_type']
            and math.dist((trader['x'],trader['y']),(target['x'],target['y']))<20)
