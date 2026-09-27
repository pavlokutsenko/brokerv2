"""Short, native-query-checked departure from an inflated planning margin."""
import math
import time


def escape(client,nav,guard,log,deadline):
    source=client.position()
    choices=sorted(nav.nodes.values(),key=lambda p:math.dist(source[:2],p))
    attempts=0;blocked=0
    for goal in choices:
        distance=math.dist(source[:2],goal)
        if distance<30 or distance>250:
            continue
        if client.cancelled() or time.monotonic()>=deadline:
            return False
        current=client.position()
        if not nav.clear_forbidden(tuple(current[:2]),goal):
            log({'type':'escape_skip','reason':'hard_exclusion','source':current,'goal':goal})
            continue
        if not guard.clear(current,goal):
            blocked+=1
            if blocked<=20:
                log({'type':'escape_blocked','source':source,'goal':goal,'hit':guard.last_hit})
            if not guard.clear_level_escape(client.position(),goal):
                if not guard.clear_outward_escape(client.position(),goal): continue
                log({'type':'escape_outward_clear','source':source,'goal':goal})
            log({'type':'escape_level_clear','source':source,'goal':goal})
        attempts+=1
        client.move(goal)
        start=time.monotonic()
        while time.monotonic()-start<2.2:
            current=client.position()
            log({'type':'sample','phase':'escape','position':current})
            if math.dist(current[:2],goal)<18 or client.cancelled() or time.monotonic()>=deadline:
                break
            time.sleep(.08)
        client.stop()
        current=client.position()[:2]
        log({'type':'escape','source':source,'goal':goal,'position':current,'attempt':attempts})
        if nav.clear(current,current):
            return True
        if attempts>=3:
            return False
    return False
