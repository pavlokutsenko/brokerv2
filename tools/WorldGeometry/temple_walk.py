"""Movement-only temple gate check. Run with automatic collection disconnected."""
import argparse
from datetime import datetime, timezone
import json
import math
from pathlib import Path
import time

from cycle_plan import price_navigation_data
from walk_client import WalkClient
from walk_escape import escape
from walk_geometry import Navigation, rounded_route
from walk_guard import WalkGuard
from walk_recovery import follow_with_recovery
from walk_follow import follow
from city_maps import load_navigation


def run(pid, destination, output, duration, ordinary_probe=False):
    data = price_navigation_data(load_navigation('Giran'))
    output.parent.mkdir(parents=True, exist_ok=True)
    result = {'pid': pid, 'destination': destination, 'reason': 'preparing'}
    with WalkClient(pid) as client, output.with_suffix('.motion.jsonl').open('w', encoding='utf-8', buffering=1) as stream:
        def log(row):
            stream.write(json.dumps({'at': datetime.now(timezone.utc).isoformat(), **row}) + '\n')
        try:
            client.wait_navigation_capsule()
            client.navigation_rules=data.get('navigationRules',{})
            result['source'] = client.position()
            client.install()
            guard = WalkGuard(client, data)
            nav = Navigation({**data,'ground':None,'obstacles':[],'unknown':[]} if ordinary_probe else data,
                             clearance=client.execution_clearance)
            if not nav.clear(client.position()[:2], client.position()[:2]):
                if not escape(client, nav, guard, log, time.monotonic() + 15):
                    result['reason'] = 'escape_unreachable'
                    return
            points = rounded_route(nav.shortest(client.position()[:2], destination), nav)
            result['points'] = points
            if ordinary_probe:
                result.update(follow(client,nav,points,log,min(30,duration),
                    lambda row:print(json.dumps(row),flush=True),None))
            else:
                result.update(follow_with_recovery(client, nav, points, log, duration,
                    lambda row: print(json.dumps(row), flush=True), guard))
            result['destination_error'] = math.dist(client.position()[:2], destination)
            result['passed'] = result['reason'] == 'completed' and result['destination_error'] <= 30
        finally:
            result['position'] = client.position()
            result['finishedAt'] = datetime.now(timezone.utc).isoformat()
            output.write_text(json.dumps(result, indent=2), encoding='utf-8')
            print(json.dumps({k: result[k] for k in ('reason', 'position', 'passed') if k in result}), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pid', type=int, required=True)
    parser.add_argument('--destination', type=float, nargs=2, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--duration', type=float, default=90)
    parser.add_argument('--ordinary-probe',action='store_true')
    args = parser.parse_args()
    if not 5 <= args.duration <= 120:
        parser.error('duration must be 5..120 seconds')
    run(args.pid, args.destination, args.output, args.duration,args.ordinary_probe)
