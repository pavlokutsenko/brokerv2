"""Read-only acceptance evidence for the owned Collector process generation."""
import argparse
from collections import Counter
from datetime import datetime, timezone
import json
import math
from pathlib import Path
import re
import sqlite3
import statistics


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def timestamp(value):
    return datetime.fromisoformat(value.replace('Z', '+00:00'))


def lines(path):
    for line in path.read_text(encoding='utf-8-sig').splitlines():
        try:
            yield json.loads(line)
        except ValueError:
            continue  # A live writer may not have finished its last line.


def audit(watch_path):
    watch = read(watch_path)
    started = timestamp(watch['collection_started_utc'])
    base = watch_path.parents[2]
    runs = base / 'collection' / watch['profile_id'].replace('-', '') / 'runs'
    logs = [r for r in lines(Path(watch['collector_log'])) if timestamp(r['Time']) >= started]
    reads = [r for r in logs if 'durable snapshot ' in r['Message']]
    read_ids = {re.search(r'durable snapshot (\S+)', r['Message']).group(1) for r in reads}
    acks = {re.search(r'snapshot (\S+)', r['Message']).group(1) for r in logs
            if 'server accepted individual trader' in r['Message']}
    history = {re.search(r'snapshot (\S+)', r['Message']).group(1) for r in logs
               if 'server retained historical read' in r['Message']}
    events = []
    detours = []
    redundant = []
    arcs = []
    passing_arcs = []
    section_spans = []
    for path in runs.glob('route-*.motion.jsonl'):
        if datetime.fromtimestamp(path.stat().st_mtime, timezone.utc) < started:
            continue
        rows = [r for r in lines(path) if timestamp(r['at']) >= started]
        events.extend(rows)
        if any(r['type']=='reader_session' for r in rows):
            moves=[r for r in rows if r['type']=='move_command']
            result_path=path.with_name(path.name.replace('.motion.jsonl','.json'))
            result=read(result_path) if result_path.exists() else {}
            section_spans.append({'file':path.stem,'firstMove':moves[0]['at'] if moves else None,
                                  'lastMove':moves[-1]['at'] if moves else None,
                                  'finishedAt':result.get('finishedAt')})
        plan_path = path.with_name(path.name.replace('.motion.jsonl', '.plan.json'))
        if not rows or not plan_path.exists():
            continue
        plan = read(plan_path)
        anchors = {a['key']: a for a in plan['anchors']}
        for row in rows:
            if row['type'] == 'radar_detour_plan':
                detours.append(row['key'])
                if row['key'] in anchors:
                    redundant.append(row['key'])
        for index, row in enumerate(rows):
            if row['type'] not in ('radar_detour_plan', 'recheck_plan') or not row.get('passing_arc'):
                continue
            pass_points = row['points']
            if len(pass_points) < 14:
                continue
            pass_distance = [0.0]
            for first, second in zip(pass_points, pass_points[1:]):
                pass_distance.append(pass_distance[-1] + math.dist(first, second))
            curve = pass_points[-14:-1]
            # Three non-collinear arc points identify its planned circle.
            a, c, d = curve[0], curve[6], curve[-1]
            divisor = 2 * (a[0]*(c[1]-d[1]) + c[0]*(d[1]-a[1]) + d[0]*(a[1]-c[1]))
            if abs(divisor) < 1e-8:
                continue
            squares = [p[0]**2 + p[1]**2 for p in (a, c, d)]
            center = ((squares[0]*(c[1]-d[1])+squares[1]*(d[1]-a[1])+squares[2]*(a[1]-c[1]))/divisor,
                      (squares[0]*(d[0]-c[0])+squares[1]*(a[0]-d[0])+squares[2]*(c[0]-a[0]))/divisor)
            samples = []
            for sample in rows[index+1:]:
                if sample['type'] == 'result':
                    break
                if (sample['type'] == 'sample' and 'arc' in sample
                        and pass_distance[-14] <= sample['arc'] <= pass_distance[-2]):
                    samples.append(sample)
            if len(samples) >= 2:
                radii = [math.dist(s['position'][:2], center) for s in samples]
                passing_arcs.append({'key': row['key'], 'samples': len(samples),
                                     'minRadius': round(min(radii), 2), 'maxRadius': round(max(radii), 2)})
        points = plan['points']
        distances = [0.0]
        for first, second in zip(points, points[1:]):
            distances.append(distances[-1] + math.dist(first, second))
        initial = []
        for row in rows:
            if row['type'] == 'result':
                break  # Later rejoin segments use their own arc origin.
            if row['type'] == 'sample' and 'arc' in row:
                initial.append(row)
        for approach in plan.get('approaches', []):
            if approach['mode'] != 'arc':
                continue
            arc_points = approach['points'][:13]
            try:
                first = points.index(arc_points[0])
                last = points.index(arc_points[-1], first)
            except ValueError:
                continue
            samples = [r for r in initial if distances[first] <= r['arc'] <= distances[last]]
            if len(samples) < 2:
                continue
            trader = anchors[approach['key']]
            radii = [math.dist(r['position'][:2], (trader['x'], trader['y'])) for r in samples]
            arcs.append({'key': approach['key'], 'samples': len(samples),
                         'minRadius': round(min(radii), 2), 'maxRadius': round(max(radii), 2)})
    goals = [math.dist(r['position'][:2], r['goal'][:2]) for r in events if r['type'] == 'move_command']
    spans=sorted((s for s in section_spans if s['firstMove']),key=lambda s:timestamp(s['firstMove']))
    boundaries=[{'previous':a['file'],'next':b['file'],
                 'ackToNextMoveSeconds':round((timestamp(b['firstMove'])-timestamp(a['finishedAt'])).total_seconds(),3),
                 'lastCommandToNextSeconds':round((timestamp(b['firstMove'])-timestamp(a['lastMove'])).total_seconds(),3)}
                for a,b in zip(spans,spans[1:]) if a['finishedAt'] and a['lastMove']]
    database = base / 'collection/markets/9B192F676F7BC594D854A850/market.sqlite3'
    with sqlite3.connect(database.as_uri() + '?mode=ro', uri=True) as connection:
        queue = connection.execute('SELECT kind,count(*),max(attempts) FROM outbox GROUP BY kind').fetchall()
    return {'at': datetime.now(timezone.utc).isoformat(), 'collectorPid': watch['collector_pid'],
            'clientPid': watch['client_pid'], 'collectionStarted': watch['collection_started_utc'],
            'exactReads': len(read_ids), 'uniqueShops': len({r['Trader'] for r in reads}),
            'priceRows': sum(int(re.search(r'exact read (\d+) rows', r['Message']).group(1)) for r in reads),
            'serverCurrentAcks': len(read_ids & acks), 'historicalReceipts': len(read_ids & history),
            'receiptsPending': sorted(read_ids - acks - history), 'outbox': queue,
            'motion': {'commands': len(goals), 'medianGoalDistance': round(statistics.median(goals), 2) if goals else None,
                       'readerSessions':dict(Counter('reused' if r['reused'] else 'installed' for r in events if r['type']=='reader_session')),
                       'sectionBoundaries':boundaries,
                       'coverageHandoffs':sum(r.get('handoff',False) for r in events if r['type']=='result'),
                       'movingPreparation':[r for r in events if r['type']=='moving_preparation_result'],
                       'movingOriginReconnects':[r for r in events if r['type']=='handoff_origin_reconnected'],
                       'sectionPreparation': [{'seconds':r['seconds'],'backgroundPlan':r['background_plan'],
                                               'backgroundGeometry':r.get('background_geometry',False),
                                               'overlapped':r.get('overlapped',False),
                                               'movingSeconds':r.get('movingSeconds',0),
                                               'targets':r['targets']} for r in events if r['type']=='section_preparation'],
                       'nearGoalsUnder12': sum(g < 12 for g in goals),
                       'redundantPlannedDetours': redundant, 'unplannedDetours': detours,
                       'results': dict(Counter(r['reason'] for r in events if r['type'] == 'result')),
                       'primaryArcs': arcs, 'individualPassingArcs': passing_arcs}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('watch', type=Path)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    result = json.dumps(audit(args.watch), ensure_ascii=False, indent=2)
    if args.output:
        args.output.write_text(result, encoding='utf-8')
    print(result)
