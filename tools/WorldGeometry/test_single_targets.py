import math
import unittest
import struct
from unittest.mock import Mock,patch
from types import SimpleNamespace
from pathlib import Path
from shapely.geometry import LineString, Point
from walk_geometry import Navigation,rounded_route
from walk_single_targets import flybys,plan_singles,add_exclusions
from walk_shops import WalkShops


class SingleTargetTests(unittest.TestCase):
    def data(self):
        return {'extent':[0,2200,0,2200],'obstacles':[],'unknown':[],
                'captured':'test','traders':[['test',500,500]]}

    def test_curved_pass_stays_away_and_has_onward_departure(self):
        nav=Navigation(self.data());center=(81100,148100)
        add_exclusions(nav,[{'x':center[0],'y':center[1]}],55)
        options=flybys(nav,center)
        self.assertTrue(options)
        for points in options:
            smooth=rounded_route(points,nav)
            distance=LineString(smooth).distance(Point(center))
            self.assertGreaterEqual(distance,55)
            self.assertLessEqual(distance,80)
            self.assertGreater(math.dist(points[0],center),180)
            self.assertGreater(math.dist(points[-1],center),180)
            self.assertGreater(math.dist(points[0],points[-1]),150)

    def test_four_corner_targets_and_return_without_touching_them(self):
        traders=[{'name':str(i),'x':80000+x,'y':147000+y,'kiosk_type':1,'object_id':i+1}
                 for i,(x,y) in enumerate([(400,400),(1800,400),(1800,1800),(400,1800)])]
        start=(81100,148100)
        route=plan_singles(self.data(),start,traders)
        self.assertEqual(len({t['corner'] for t in route['targets']}),4)
        self.assertEqual(route['points'][0],start);self.assertEqual(route['points'][-1],start)
        self.assertTrue(all(55<=t['planned_min_distance']<=85 for t in route['targets']))

    def test_streamed_out_actor_is_skipped(self):
        reader=WalkShops.__new__(WalkShops)
        def unavailable(*_): raise OSError('actor no longer mapped')
        reader.read_trader=unavailable
        self.assertIsNone(reader.fresh(None,{}))

    def test_remote_trader_class_differs_from_local_pawn(self):
        memory=Mock()
        walk=SimpleNamespace(m=memory,base=0x100000,pid=123,initial_selected=0,
                             world={'player_actor':0x5000},s=Mock())
        walk.s.describe.return_value={'address':'0x9000','name':'CharacterPlayer_C',
                                      'class':'BlueprintGeneratedClass'}
        with patch('walk_shops.read_object',return_value=0x9000), \
                patch('walk_shops.ShopHooks'),patch('walk_shops.ShopWire'):
            reader=WalkShops(walk,Path('test'))
        actor=0x1000;root=0x2000
        memory.u64.side_effect=lambda addr: {actor+0x10:0x9000,actor+0x1a0:root}[addr]
        memory.i32.side_effect=lambda addr:42 if addr==actor+0x550 else 1
        memory.fstring.return_value='Alice'
        memory.read.return_value=struct.pack('<3d',10,20,30)
        self.assertEqual(reader.fresh(memory,{'actor':hex(actor)})['name'],'Alice')
        memory.u64.side_effect=lambda _:0x8000  # CharacterMain_C or a reused non-player actor
        self.assertIsNone(reader.fresh(memory,{'actor':hex(actor)}))


if __name__=='__main__': unittest.main()
