"""Range, identity and reply checks independent of live transport."""
import math
import unicodedata

RADAR_DISCOVERY_RADIUS = 3000


def trader_key(name):
    return unicodedata.normalize('NFKC', name).strip().casefold()


def generation(trader):
    return trader_key(trader['name']), trader['object_id'], trader['kiosk_type']


def choose_candidate(candidates,position,velocity,radius,priority_keys=()):
    """Service route anchors first, then the shop whose passing window closes first."""
    speed2=sum(v*v for v in velocity)
    def score(trader):
        offset=(position[0]-trader['x'],position[1]-trader['y'])
        distance=math.hypot(*offset)
        if speed2>25:
            dot=sum(offset[k]*velocity[k] for k in (0,1))
            exit_time=(-dot+math.sqrt(max(0,dot*dot+speed2*(radius*radius-distance*distance))))/speed2
        else:
            exit_time=distance
        return (trader_key(trader['name']) not in priority_keys,exit_time,distance)
    return min(candidates,key=score)


def eligible(expected, current, position, radius):
    return (current is not None and generation(expected) == generation(current)
            and expected['actor'] == current['actor']
            and current['kiosk_type'] in (1,3,8)
            and current['object_id'] > 0 and bool(trader_key(current['name']))
            and all(math.isfinite(v) for v in (*position,current['x'],current['y'],current['z']))
            and math.dist(position[:2],(current['x'],current['y'])) <= radius
            and abs(position[2]-current['z']) <= 60)


def server_nearby(trader, server_position, server_age, radius, local_position=None,stationary_probe=False):
    if server_position is None or not 0 <= server_age <= 8:
        return False
    distance = math.dist(server_position[:2], (trader['x'], trader['y']))
    if server_age>.8:
        # A stopped pawn does not necessarily receive new movement packets,
        # even after zero-distance moves. Only the bounded stationary read
        # phase can use an older origin: both positions must already be in
        # range and agree closely. The server validates the ordinary request.
        return (stationary_probe and local_position is not None and distance<=radius
                and math.dist(local_position[:2],(trader['x'],trader['y']))<=min(radius,85)
                and math.dist(server_position[:2],local_position[:2])<=40
                and abs(server_position[2]-local_position[2])<=15)
    if distance <= radius:
        return True
    # The server position packet describes an earlier point on the same
    # ordinary movement. A short flyby can finish before its next packet is
    # observed. Allow that lag only while the live pawn is well inside range;
    # the game server still validates each unmodified shop request.
    return (local_position is not None
            and math.dist(local_position[:2], (trader['x'], trader['y'])) <= min(radius, 85)
            and distance <= radius + min(40, 160 * server_age))


def shop_rows(capture, wire, expected):
    side = 'buy' if expected['kiosk_type']==3 else 'sell'
    if capture['side'] != side or capture['copied_count'] != capture['count']:
        raise ValueError('shop side/count mismatch')
    rows = capture['rows']
    if not rows:
        # An empty reply can race a shop closing. It is not a successful price
        # verification and never grants permission to remove market offers.
        raise ValueError('empty shop reply; state requires verification')
    if len(rows) != capture['count'] or any(r['item_id']<=0 for r in rows):
        raise ValueError('invalid or truncated shop rows')
    if wire is not None:
        if wire.get('side','sell') != side or wire['object_id'] != expected['object_id'] or wire['row_count'] != len(rows):
            raise ValueError('wire/event mismatch')
        for event, packet in zip(rows, wire['rows']):
            fields = [('item_id','item_id')]
            if side == 'sell':
                fields.extend((('item_object_id','item_object_id'),('enchant_level','enchant')))
            if any(event[a] != packet[b] for a,b in fields):
                raise ValueError('wire/event row identity mismatch')
            if packet['price']<0 or packet['quantity']<0:
                raise ValueError('negative wire price/count')
            if side == 'buy' and ((packet['price'] & 0xffffffff) != (event['price'] & 0xffffffff)
                                  or (packet['quantity'] & 0xffffffff) != (event['buy_count'] & 0xffffffff)):
                raise ValueError('BE buy/event value mismatch')
        if side == 'buy':
            return [{**packet, 'item_object_id':event['item_object_id'], 'enchant':event['enchant_level'],
                     'base_price':event['base_price']} for event,packet in zip(rows,wire['rows'])], 'wire_int64'
        return wire['rows'], 'wire_int64'
    # Other response layouts have not been decoded independently yet.
    return rows, 'client_int32_unverified'
