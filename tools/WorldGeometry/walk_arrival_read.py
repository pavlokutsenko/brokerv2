"""Give the existing reader a short request window before settling a shop stop."""
import time
import math


def arrival_read(client,shops,key,log,seconds=.75):
    started=time.monotonic();deadline=started+seconds;last_move=0
    previous=getattr(shops,'read_hold_key',None)
    shops.read_hold_key=key;shops.active.set()
    requests=shops.stats['requests']
    with shops.lock:
        for token in list(shops.retry):
            if token[0]==key: shops.retry[token]=0
    try:
        while time.monotonic()<deadline:
            if client.cancelled(): return 'cancelled'
            if shops.error: return 'shop_error'
            if key in shops.captured_keys: return 'captured'
            if key in getattr(shops,'unavailable_keys',set()): return 'temporarily_unavailable'
            position=client.position()
            selected=client.m.u64(client.world['controller']+0x898)
            if selected!=client.initial_selected and not shops.allowed_target(selected):
                return 'target_changed_externally'
            if time.monotonic()-last_move>=.12:
                # Ordinary zero-distance movement supplies the action lane;
                # unlike a stop wait it keeps requests/discovery active.
                client.move(position[:2]);last_move=time.monotonic()
            time.sleep(.02)
        return 'no_exact_reply'
    finally:
        shops.read_hold_key=previous
        live=getattr(shops,'live_targets',{}).get(key)
        position=client.position()
        log({'type':'arrival_read','key':key,'captured':key in shops.captured_keys,
             'seconds':round(time.monotonic()-started,3),
             'requests':shops.stats['requests']-requests,'actor_present':live is not None,
             'position':position,
             'distance':math.dist(position[:2],(live['x'],live['y'])) if live else None,
             'vertical_gap':abs(position[2]-live['z']) if live else None,
             'server_age':time.monotonic()-getattr(shops,'server_at',0)})
