"""Read-only comparison of a complete center radar and an active server roster."""
import argparse
import json
from datetime import datetime
from pathlib import Path
from shapely.geometry import Point, Polygon


def audit(broker, roster, city):
    proof = broker['native_radar']
    if not proof['complete'] or not roster['activeRosterComplete']:
        raise ValueError('Both independent radar and active server roster must be complete')
    zone = Polygon(city['collectionZone']['polygon'])
    native = {r['name'].strip().upper(): r for r in broker['native_state_observations']
              if r['kiosk_type'] in (1, 3, 8)}
    covered = {k: r for k, r in native.items() if zone.covers(Point(r['x'], r['y']))}
    active = {r['traderKey'].strip().upper(): r for r in roster['traders']
              if r['isActive'] and r['kioskType'] in (1, 3, 8)}
    captured = datetime.fromisoformat(proof['observed_at'])
    extra = []
    for key in sorted(active.keys() - covered.keys()):
        r = active[key]
        x, y = r['worldX'], r['worldY']
        inside = x is not None and y is not None and zone.covers(Point(x, y))
        extra.append({'key': key, 'inside': inside,
                      'newerThanRadar': datetime.fromisoformat(r['lastSeenAtUtc'].replace('Z', '+00:00')) >= captured,
                      'state': r['state'], 'lastSeenAtUtc': r['lastSeenAtUtc']})
    return {'pid': proof['pid'], 'capture': proof['observed_at'],
            'observer': [proof['collector_x'], proof['collector_y']],
            'nativeIdentities': proof['identity_count'], 'nativeTraders': len(native),
            'nativeInsideZone': len(covered), 'serverActive': len(active),
            'missingOnServer': sorted(covered.keys() - active.keys()), 'extraOnServer': extra,
            'corners': {k: {'native': k in covered, 'serverActive': k in active}
                        for k in ('CAPITANMORGAN', 'XAWKNAGIBATOR')}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('broker', type=Path)
    parser.add_argument('roster', type=Path)
    parser.add_argument('--city', type=Path, default=Path(__file__).parents[2] / 'maps/Giran/city.json')
    args = parser.parse_args()
    read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
    print(json.dumps(audit(read(args.broker), read(args.roster), read(args.city)), ensure_ascii=False, indent=2))
