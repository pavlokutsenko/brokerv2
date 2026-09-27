"""Merge local verification generations into an executing short route."""
from datetime import datetime
from collection_zone import in_collection_zone
from walk_shop_policy import trader_key


def apply_radar_target(shops, row, log):
    if not in_collection_zone(getattr(shops,'collection_zone',None),row['x'],row['y']): return
    key=trader_key(row['name']);oid=int(row['object_id'])
    if not key or oid<=0 or int(row['kiosk_type']) not in (1,3,8): return
    closed=shops.closed_stamps.get(key)
    if closed:
        seen=datetime.fromisoformat(row['observed_at']) if row.get('observed_at') else None
        if seen is None or seen<=closed[1]: return
    existing=shops.dynamic_targets.get(key) or next(
        (t for t in shops.tracked_targets if t['key']==key),None)
    revision=row.get('verification_revision')
    previous=existing.get('verification_revision') if existing else None
    changed=revision is not None and previous is not None and str(revision)!=str(previous)
    reopening=bool(closed) or bool(row.get('reopened'))
    if key in shops.dynamic_targets and not changed and not closed: return
    if not changed and not reopening and (key in shops.broker_keys or key in shops.allowed_keys): return
    if changed or reopening:
        # A close and reopen may fit between two radar frames. The frozen
        # verification token still invalidates the old in-flight native read.
        with shops.lock:
            shops.closed_stamps.pop(key,None);shops.unavailable_keys.discard(key)
            shops.captured_keys.discard(key);shops.requested_keys.discard(key)
            shops.dynamic_detour_attempted.discard(key)
            shops.done={token for token in shops.done if token[0]!=key}
            shops.retry={token:at for token,at in shops.retry.items() if token[0]!=key}
            for pending_id,pending in list(getattr(shops,'pending',{}).items()):
                if trader_key(pending['trader']['name'])==key and str(pending.get('verification_revision'))!=str(revision):
                    shops.pending.pop(pending_id,None)
            if shops.candidate and trader_key(shops.candidate['name'])==key: shops.candidate=None
            shops.reopened_keys.add(key)
    target={**row,'key':key,'reopened':reopening}
    shops.dynamic_targets[key]=target
    shops.dynamic_seen_live.pop(key,None)
    shops.allowed_keys.add(key);shops.allowed_object_ids.add(oid)
    log({'type':'radar_recheck' if changed or reopening else 'radar_new',
         'key':key,'object_id':oid,'x':row['x'],'y':row['y'],'verification_revision':revision})
