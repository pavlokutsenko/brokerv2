import unittest
import time
import math
from types import SimpleNamespace

from cycle_revisit import select_revisit_targets,unavailable_after_approach,add_late_broker_misses,initial_revisit_targets,add_live_radar_targets,next_available_target
from cycle_recheck_plan import recheck_route
from walk_follow import Path
from walk_shops import WalkShops


class RevisitTests(unittest.TestCase):
    def test_new_goal_enters_nearest_first_without_cooldown(self):
        pending={'new':{'key':'new','x':1,'y':0},'broker':{'key':'broker','x':100,'y':0}}
        self.assertEqual(next_available_target(pending,(0,0))['key'],'new')

    def test_local_section_cannot_be_extended_by_future_market_targets(self):
        now=time.monotonic()
        assigned={'key':'assigned','x':200,'y':0,'seen_at':now}
        future={'key':'future','x':10,'y':0,'seen_at':now,'admitted_at':now}
        shops=SimpleNamespace(tracked_targets=[assigned],dynamic_targets={'future':future},
            dynamic_seen_live={'assigned':assigned,'future':future},trace_actor_present=set(),
            requested_keys=set(),captured_keys=set(),local_section_mode=True,
            local_section_keys={'assigned'},collection_zone=None)
        targets=initial_revisit_targets(shops,{'deferred':[],'anchors':[]},(0,0))
        self.assertEqual([t['key'] for t in targets],['assigned'])
        pending={}
        self.assertEqual(add_live_radar_targets(pending,shops,set(),(0,0),now),['assigned'])
        self.assertNotIn('future',pending)

    def test_assigned_unseen_broker_shop_is_approached_but_unconfirmed_radar_shop_is_not(self):
        shops=SimpleNamespace(tracked_targets=[{'key':'unseen','x':300,'y':0},
                {'key':'read','x':50,'y':0}],
            dynamic_targets={'radar':{'key':'radar','x':10,'y':0}},dynamic_seen_live={},
            trace_actor_present=set(),requested_keys=set(),captured_keys={'read'})
        targets=initial_revisit_targets(shops,{'deferred':[],'anchors':[]},(0,0))
        self.assertEqual([t['key'] for t in targets],['unseen'])

    def test_later_passed_broker_shop_is_revisited_without_repeating_handled_targets(self):
        keys=('late','requested','read','closed','handled','unseen')
        shops=SimpleNamespace(tracked_targets=[{'key':k} for k in keys],
            trace_actor_present={'late','read','closed','handled'},requested_keys={'requested'},
            captured_keys={'read'},unavailable_keys={'closed'})
        pending={}
        self.assertEqual(add_late_broker_misses(pending,shops,{'handled'}),['late','requested'])
        self.assertEqual(add_late_broker_misses(pending,shops,{'handled'}),[])
        pending.clear()
        self.assertEqual(add_late_broker_misses(pending,shops,{'handled','late','requested'}),[])

    def test_native_absence_skips_only_after_nearby_fresh_scans_without_a_request(self):
        now=time.monotonic();target={'key':'gone','x':0,'y':0}
        shops=SimpleNamespace(error=None,live_targets={},requested_keys=set(),captured_keys=set(),
                              absence_scans={'gone':3},last_actor_scan_at=now)
        self.assertTrue(unavailable_after_approach(shops,target,(68,0,0),now))
        self.assertFalse(unavailable_after_approach(shops,target,(101,0,0),now))
        self.assertFalse(unavailable_after_approach(shops,target,(68,0,0),now+2))
        shops.absence_scans['gone']=2
        self.assertFalse(unavailable_after_approach(shops,target,(68,0,0),now))
        shops.absence_scans['gone']=3;shops.requested_keys.add('gone')
        self.assertFalse(unavailable_after_approach(shops,target,(68,0,0),now))
        shops.requested_keys.clear();shops.live_targets['gone']=target
        self.assertFalse(unavailable_after_approach(shops,target,(68,0,0),now))

    def test_map_extent_is_not_a_boundary_for_known_broker_targets(self):
        data={'origin':[0,0],'extent':[-400,400,-400,400],'obstacles':[],'unknown':[]}
        target={'x':0,'y':900}
        points,nav,direct=recheck_route(data,(0,0),target)
        self.assertTrue(points)
        self.assertTrue(direct)
        self.assertLessEqual(math.dist(points[-1],(0,900)),69)
        self.assertTrue(nav.clear(*points))
    def test_all_misses_include_requests_without_exact_prices(self):
        targets=[{'key':str(i),'x':100+i,'y':0} for i in range(8)]
        self.assertEqual(len(select_revisit_targets(targets,{'0'},
                         {str(i) for i in range(8)},(0,0))),7)

    def test_close_start_has_a_read_position_instead_of_deferred(self):
        data={'origin':[0,0],'extent':[-400,400,-400,400],'obstacles':[],'unknown':[]}
        points,nav,direct=recheck_route(data,(30,0),{'x':0,'y':0})
        self.assertEqual(points,[(30,0),(30,0)])
        self.assertFalse(direct)

    def test_narrow_court_permits_closer_reading_position(self):
        data={'origin':[0,0],'extent':[-400,400,-400,400],
              'obstacles':[{'rings':[[[-70,-70],[70,-70],[70,70],[-70,70]],
                                      [[-55,-55],[55,-55],[55,55],[-55,55]]]}], 'unknown':[]}
        points,nav,direct=recheck_route(data,(0,0),{'x':0,'y':0})
        self.assertTrue(points)
        self.assertLessEqual(math.dist(points[-1],(0,0)),41)
        self.assertFalse(direct)
    def test_new_live_radar_shop_outside_upcoming_corridor_gets_one_detour(self):
        shops=object.__new__(WalkShops)
        shops.radius=95
        shops.collection_zone=None
        shops.dynamic_detours_enabled=True
        shops.dynamic_detour_attempted=set()
        shops.requested_keys=set()
        shops.dynamic_seen_live={
            'corridor':{'key':'corridor','x':300,'y':50,'seen_at':time.monotonic()},
            'new':{'key':'new','x':200,'y':200,'seen_at':time.monotonic()}}
        route=Path([(0,0),(1000,0)])
        self.assertEqual(shops.claim_dynamic_detour((0,0,0),route,0)['key'],'new')
        self.assertIsNone(shops.claim_dynamic_detour((0,0,0),route,0))

    def test_only_live_unrequested_anchors_return_once_nearest_first(self):
        anchors = [{'key': 'gone', 'x': 10, 'y': 0},
                   {'key': 'read', 'x': 20, 'y': 0},
                   {'key': 'far', 'x': 80, 'y': 0},
                   {'key': 'near', 'x': 30, 'y': 0}]
        selected = select_revisit_targets(anchors, {'read'},
                                          {'read', 'far', 'near'}, (0, 0), limit=2)
        self.assertEqual([t['key'] for t in selected], ['near', 'far'])

    def test_new_radar_shop_outside_zone_does_not_get_a_detour(self):
        shops=object.__new__(WalkShops)
        shops.radius=95
        shops.collection_zone={'polygon':[[-50,-50],[50,-50],[50,50],[-50,50]]}
        shops.dynamic_detours_enabled=True;shops.dynamic_detour_attempted=set();shops.requested_keys=set()
        shops.dynamic_seen_live={'outside':{'key':'outside','x':200,'y':200,'seen_at':time.monotonic()}}
        self.assertIsNone(shops.claim_dynamic_detour((0,0,0),Path([(0,0),(1000,0)]),0))
