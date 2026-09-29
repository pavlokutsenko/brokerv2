"""Cancellable stationary interval after a durable exact shop capture."""
import math
import time


def wait_after_capture(shops, client, log, progress=None):
    elapsed=0.0
    resume_active=shops.active.is_set()
    while True:
        with shops.lock:
            if not shops.pause_queue:
                if resume_active and not client.cancelled() and not shops.error:
                    shops.active.set()
                return 'ready',elapsed
            key=shops.pause_queue.popleft()
        if client.cancelled(): return 'cancelled',elapsed
        if shops.error: return 'shop_error',elapsed
        seconds=shops.trader_pause_seconds
        if seconds<=0: continue
        started=time.monotonic()
        outcome='ready'
        try:
            client.pause_for_plan()
            log({'type':'trader_pause','key':key,'seconds':seconds})
            deadline=time.monotonic()+seconds
            last_report=-1
            while time.monotonic()<deadline:
                if client.cancelled():
                    outcome='cancelled';break
                if shops.error:
                    outcome='shop_error';break
                remaining=math.ceil(deadline-time.monotonic())
                if progress and remaining!=last_report:
                    progress({'detail':f'Pause after trader: {remaining}s',
                              'shops':shops.stats['captured_shops'],'exact':shops.stats['exact_shops']})
                    last_report=remaining
                time.sleep(min(.1,max(.01,deadline-time.monotonic())))
        finally:
            spent=time.monotonic()-started
            shops.pause_seconds_spent+=spent
            elapsed+=spent
        if outcome!='ready': return outcome,elapsed
