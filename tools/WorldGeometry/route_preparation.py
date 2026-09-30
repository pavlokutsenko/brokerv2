"""Offline next-section planning. This module never opens or commands a client."""
import hashlib
import json
import math
from pathlib import Path
from shapely import wkb
from shapely.errors import ShapelyError
from shapely.geometry import Point
from shapely.ops import unary_union
from city_maps import load_navigation
from collection_zone import in_collection_zone
from cycle_plan import price_navigation_data, price_route
from walk_geometry import Navigation, rounded_route
from walk_shop_policy import trader_key


def fingerprint(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def targets_for(config, data):
    return [{**t, 'key': trader_key(t['name'])} for t in config['targets']
            if in_collection_zone(data.get('collectionZone'), t['x'], t['y'])]


def identity(config):
    # ObjectID belongs to one client process; it cannot identify a market plan
    # shared between accounts. The live reader still rebinds and verifies it.
    geometry_keys=('traderKey','name','x','y','kiosk_type','verification_revision','local_revision')
    return {'city': config['city'], 'targets': [
        {key:target.get(key) for key in geometry_keys} for target in config['targets']]}


def dump_navigation(nav, clearance):
    return {'clearance': clearance, 'cell': nav.cell, 'origin': nav.origin,
            'extent': nav.extent, 'region': nav.region.wkb_hex,
            'forbidden': nav.forbidden.wkb_hex, 'blocked': nav.blocked.wkb_hex,
            'nodes': [[*key, *point] for key, point in nav.nodes.items()]}


def restore_navigation(value):
    nav = object.__new__(Navigation)
    nav.cell = value['cell']; nav.origin = value['origin']; nav.extent = value['extent']
    nav.region = wkb.loads(value['region'], hex=True)
    nav.forbidden = wkb.loads(value['forbidden'], hex=True)
    nav.blocked = wkb.loads(value['blocked'], hex=True)
    nav.free = nav.region.difference(nav.blocked)
    nav.nodes = {(r[0], r[1]): (r[2], r[3]) for r in value['nodes']}
    nav.edges = {}
    return nav


def prepare(config, data=None):
    data = data or load_navigation(config['city'])
    route_data = price_navigation_data(data)
    reused=prepared_navigation(config,data,24)
    planning = reused[0].fork() if reused else Navigation(route_data)
    execution = reused[1].fork() if reused else Navigation(route_data, clearance=24)
    start = config['planningStart']
    route = price_route(route_data, start, targets_for(config, data), navigation=planning)
    return {'schema': 1, 'mapHash': fingerprint(data), 'targetHash': fingerprint(identity(config)),
            'route': route, 'planning': dump_navigation(planning, 55),
            'execution': dump_navigation(execution, 24)}


def prepared_navigation(config, data, execution_clearance):
    cached = config.get('preparedPlan')
    if not isinstance(cached,dict) or cached.get('schema') != 1 or cached.get('mapHash') != fingerprint(data):
        return None
    if not isinstance(cached.get('execution'),dict) or not isinstance(cached.get('planning'),dict):
        return None
    if cached['execution'].get('clearance') != execution_clearance or cached['planning'].get('clearance') != 55:
        return None
    try:
        return restore_navigation(cached['planning']), restore_navigation(cached['execution'])
    except (KeyError, TypeError, ValueError, ShapelyError):
        return None


def accept_prepared(config, data, start, execution_clearance, navigation=None,skip_keys=None):
    cached = config.get('preparedPlan')
    if not isinstance(cached,dict):
        return None
    navigation = navigation or prepared_navigation(config, data, execution_clearance)
    if navigation is None:
        return None
    # Any changed coordinate, shop generation, identity or section membership
    # invalidates the speculation. Prices still validate fresh native identity.
    if cached.get('targetHash') != fingerprint(identity(config)):
        return None
    try:
        planning, execution = navigation
        route = json.loads(json.dumps(cached['route']))
        unread=[a for a in route['anchors'] if a['key'] not in (skip_keys or set())]
        if skip_keys and route['anchors'] and not unread:
            route['points']=[]
            return route,planning,execution
        points = route['points']
        if not points:
            return None
        # Rechecks can end far from the main path's predicted endpoint. Join
        # the first actual passing arc, never walk back to that speculative
        # origin. Keep every remaining anchor/pass in the prepared body.
        approaches=route.get('approaches',[])
        approach=next((a for a in approaches if a['key'] not in (skip_keys or set())),{})
        entry=approach.get('points',[None])[0]
        if entry is not None:
            first=next((i for i,p in enumerate(points) if math.dist(p,entry)<.01),None)
            if first is None:return None
            points=points[first:]
        elif unread:
            target=unread[0]
            first=next((i for i,p in enumerate(points) if math.dist(p,(target['x'],target['y']))<=85),None)
            if first is None:return None
            points=points[first:]
        connector_nav = planning.fork()
        if route['blockers']:
            connector_nav.add_blocker(unary_union([Point(t['x'], t['y']).buffer(55)
                                                   for t in route['blockers']]))
        connector = rounded_route(connector_nav.shortest(start, points[0]), connector_nav)
        route['points'] = [*connector[:-1], *points]
        if not all(connector_nav.clear(a, b) for a, b in zip(route['points'], route['points'][1:])):
            return None
        return route, planning, execution
    except (KeyError, TypeError, ValueError, RuntimeError, ShapelyError):
        return None


def run(input_file, output):
    from worker_progress import check_stop
    config = json.loads(Path(input_file).read_text(encoding='utf-8-sig'))
    check_stop()
    result = prepare(config)
    check_stop()
    output = Path(output)
    temporary = output.with_suffix('.tmp')
    temporary.write_text(json.dumps(result, ensure_ascii=False), encoding='utf-8')
    temporary.replace(output)
