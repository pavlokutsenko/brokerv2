"""Positive shop closure signals; absence alone cannot cancel a distant visit."""
from walk_shop_policy import trader_key
from datetime import datetime,timezone


def close_visit(shops,key,object_id,log,source,observed_at=None):
    target=shops.dynamic_targets.get(key) or next((t for t in shops.tracked_targets if t['key']==key),None)
    if target is None or target['object_id']!=object_id or key in shops.unavailable_keys: return False
    with shops.lock:
        shops.unavailable_keys.add(key)
        shops.dynamic_targets.pop(key,None);shops.dynamic_seen_live.pop(key,None)
        shops.closed_stamps[key]=(object_id,datetime.fromisoformat(observed_at) if observed_at else datetime.now(timezone.utc))
        if key not in {t['key'] for t in shops.tracked_targets}:
            shops.allowed_keys.discard(key);shops.allowed_object_ids.discard(object_id)
        if shops.candidate and trader_key(shops.candidate['name'])==key: shops.candidate=None
        # A reply to the old open generation can still arrive after closure.
        # It must not be logged/committed as a successful current read.
        getattr(shops,'pending',{}).pop(object_id,None)
    log({'type':'shop_closed','key':key,'object_id':object_id,'source':source})
    return True


def native_closed(shops,memory,previous,current,log):
    for key,target in previous.items():
        if key in current or key in shops.unavailable_keys: continue
        fresh=shops.fresh(memory,target)  # Existing validated class/root/OID/type offsets.
        if (fresh and fresh['object_id']==target['object_id'] and
                trader_key(fresh['name'])==key and fresh['kiosk_type']==0):
            close_visit(shops,key,fresh['object_id'],log,'native kiosk=0')


def native_closed_observations(shops,memory,actors,log):
    # Same scoped actor scan, including candidates that stood before their
    # first trading observation. Revalidate class/root/name/OID before cancel.
    for actor in actors:
        if actor.get('kiosk_type')!=0: continue
        key=trader_key(actor['name'])
        if key in shops.unavailable_keys: continue
        target=shops.dynamic_targets.get(key) or next((t for t in shops.tracked_targets if t['key']==key),None)
        if target is None or target['object_id']!=actor['object_id']: continue
        fresh=shops.fresh(memory,actor)
        if (fresh and fresh['kiosk_type']==0 and fresh['object_id']==target['object_id']
                and trader_key(fresh['name'])==key):
            close_visit(shops,key,fresh['object_id'],log,'native kiosk=0')
