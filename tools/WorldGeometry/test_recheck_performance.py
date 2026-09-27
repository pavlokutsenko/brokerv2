import math
import time
import unittest
from types import SimpleNamespace
from unittest.mock import patch
from shapely.geometry import Point

from cycle_recheck_plan import recheck_route
from cycle_revisit import add_live_radar_targets
from walk_geometry import Navigation
from walk_shops import WalkShops
from walk_follow import Path, follow


class RecheckPerformanceTests(unittest.TestCase):
    def setUp(self):
        self.data={'origin':[0,0],'extent':[-500,500,-500,500],
                   'obstacles':[],'unknown':[],
                   'hardExclusions':[{'bounds':[200,250,-50,50]}]}

    def test_reuse_does_not_leak_learned_contacts_or_clear_hard_traps(self):
        base=Navigation(self.data,clearance=24)
        with patch('cycle_recheck_plan.Navigation',side_effect=AssertionError('city rebuilt')):
            points,nav,direct=recheck_route(self.data,(-200,0),{'x':0,'y':0},navigation=base)
        nav.add_blocker(Point(-120,0).buffer(35))
        self.assertFalse(nav.clear((-120,0),(-120,0)))
        self.assertTrue(base.clear((-120,0),(-120,0)))
        self.assertFalse(nav.clear_forbidden((150,0),(300,0)))
        self.assertFalse(base.clear_forbidden((150,0),(300,0)))
        self.assertFalse(direct)
        self.assertAlmostEqual(math.dist(points[-1],(0,0)),68)

    def test_nearest_clear_approach_does_not_run_other_23_searches(self):
        base=Navigation(self.data,clearance=24)
        with patch.object(Navigation,'shortest',autospec=True,wraps=None) as shortest:
            shortest.side_effect=lambda n,a,b:[a,b]
            points,nav,direct=recheck_route(self.data,(-200,0),{'x':0,'y':0},navigation=base)
        self.assertEqual(shortest.call_count,1)
        self.assertAlmostEqual(sum(math.dist(a,b) for a,b in zip(points,points[1:])),132)

    def test_new_live_shops_are_added_during_revisits_once_and_within_3000(self):
        now=time.monotonic()
        def target(key,x,age=0): return {'key':key,'x':x,'y':0,'seen_at':now-age}
        shops=SimpleNamespace(dynamic_seen_live={t['key']:t for t in
            (target('new',200),target('read',210),target('tried',220),
             target('stale',230,3),target('far',3001),target('outside',-400))},
            captured_keys={'read'},collection_zone={'polygon':[[-50,-50],[3100,-50],[3100,50],[-50,50]]})
        pending={}
        self.assertEqual(add_live_radar_targets(pending,shops,{'tried'},(0,0),now),['new'])
        self.assertEqual(add_live_radar_targets(pending,shops,{'tried'},(0,0),now),[])

    def test_fifth_new_shop_still_gets_a_detour(self):
        shops=object.__new__(WalkShops)
        shops.collection_zone=None;shops.radius=95;shops.dynamic_detours_enabled=True
        shops.dynamic_detour_attempted={'a','b','c','d'};shops.requested_keys=set()
        shops.dynamic_seen_live={'fifth':{'key':'fifth','x':100,'y':200,'seen_at':time.monotonic()}}
        self.assertEqual(shops.claim_dynamic_detour((0,0,0),Path([(0,0),(1000,0)]),0)['key'],'fifth')

    def test_native_admitted_shop_survives_a_long_last_approach(self):
        now=time.monotonic()
        heldin={'key':'heldin','x':81195,'y':147995,'seen_at':now-11,'admitted_at':now-11}
        cache_only={**heldin,'key':'cache_only'};cache_only.pop('admitted_at')
        shops=SimpleNamespace(dynamic_seen_live={'heldin':heldin,'cache_only':cache_only},
                              captured_keys=set(),collection_zone=None)
        pending={}
        self.assertEqual(add_live_radar_targets(pending,shops,set(),(82416,147896),now),['heldin'])
        self.assertEqual(add_live_radar_targets({},shops,{'heldin'},(82416,147896),now),[])

    def test_radar_detour_range_includes_3000_but_excludes_3001(self):
        shops=object.__new__(WalkShops)
        shops.collection_zone=None;shops.radius=95;shops.dynamic_detours_enabled=True
        shops.dynamic_detour_attempted=set();shops.requested_keys=set()
        shops.dynamic_seen_live={'new':{'key':'new','x':3000,'y':0,'seen_at':time.monotonic()},
                                 'far':{'key':'far','x':3001,'y':0,'seen_at':time.monotonic()}}
        route=Path([(0,0),(1000,0)])
        self.assertEqual(shops.claim_dynamic_detour((0,0,0),route,0)['key'],'new')
        self.assertIsNone(shops.claim_dynamic_detour((0,0,0),route,0))

    def test_captured_individual_handoff_has_no_stop_or_sleep(self):
        shops=SimpleNamespace(active=SimpleNamespace(set=lambda:None,clear=lambda:None),
                              error=None,captured_keys={'read'},ignored_outside_keys=set())
        client=SimpleNamespace(shops=shops,read_goal_key='read',position=lambda:(0,0,0),
                               cancelled=lambda:False,stop=lambda:(_ for _ in ()).throw(AssertionError('stop after exact reply')))
        with patch('walk_follow.time.sleep') as sleep:
            result=follow(client,None,[(0,0),(200,0)],lambda _:None)
        self.assertIsNone(result['stop_drift'])
        sleep.assert_not_called()
        self.assertTrue(result['handoff'])

    def test_arrived_individual_stop_can_start_waiting_for_reply_immediately(self):
        shops=SimpleNamespace(active=SimpleNamespace(set=lambda:None,clear=lambda:None),
                              error=None,captured_keys=set(),ignored_outside_keys=set())
        client=SimpleNamespace(shops=shops,read_goal_key='read',position=lambda:(0,0,0),
                               cancelled=lambda:False,stop=lambda:None)
        with patch('walk_follow.time.sleep') as sleep,patch('walk_follow.arrival_read') as read:
            result=follow(client,None,[(0,0),(0,0)],lambda _:None)
        self.assertEqual(result['reason'],'completed')
        sleep.assert_called_once_with(.1)
        read.assert_called_once()


if __name__=='__main__': unittest.main()
