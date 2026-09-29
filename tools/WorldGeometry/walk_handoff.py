"""Reconnect a witnessed moving handoff without dropping any unread path."""
import math
import time
from walk_geometry import rounded_route


def reconnect_handoff(client, nav, points, current, log):
    at = getattr(client, 'last_handoff_at', None)
    source = getattr(client, 'last_handoff_position', None)
    if at is None or source is None or not points:
        return points
    age = time.monotonic() - at
    drift = math.dist(current[:2], points[0])
    displacement = math.dist(current[:2], source[:2])
    # Only the preceding successful ordinary handoff authorizes this local
    # correction. Unexpected jumps, stale witnesses and long detours retain
    # the follower's existing deviation/recovery behavior.
    if not 0 <= age <= 2.5 or not 35 < drift <= 260 or \
            displacement > max(120, 180 * age + 25):
        return points
    try:
        connector = rounded_route(nav.shortest(current[:2], points[0]), nav)
        length = sum(math.dist(a, b) for a, b in zip(connector, connector[1:]))
        if not connector or length > 600 or not all(
                nav.clear(a, b) for a, b in zip(connector, connector[1:])):
            return points
    except RuntimeError:
        return points
    # Preserve every original point: reconnecting must not skip an unread
    # approach, a gate or a room. Every issued move still uses the capsule guard.
    result = [*connector[:-1], *points]
    log({'type': 'handoff_origin_reconnected', 'position': current,
         'original_start': points[0], 'drift': round(drift, 2),
         'handoff_age': round(age, 3), 'connector_length': round(length, 2)})
    return result
