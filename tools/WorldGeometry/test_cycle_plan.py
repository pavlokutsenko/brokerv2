import math
import random
import unittest
from shapely.geometry import LineString,Point
from cycle_plan import center_route,price_route
from walk_geometry import Navigation

DATA={'extent':[-1000,2000,-1000,2000], 'obstacles':[{'rings':[[[200,-200],[300,-200],[300,700],[200,700]]]}], 'unknown':[]}
class CyclePlanTests(unittest.TestCase):
    def test_random_center_is_reachable_and_varies(self):
        nav=Navigation(DATA);previous=None;seen=[]
        for seed in range(8):
            plan=center_route(DATA,(79800,147000),(80000,147000),previous,random.Random(seed))
            destination=plan['destination']
            self.assertLessEqual(math.dist(destination,(80000,147000)),470)
            if previous:self.assertGreaterEqual(math.dist(previous,destination),100)
            self.assertTrue(all(nav.clear(a,b) for a,b in zip(plan['points'],plan['points'][1:])))
            seen.append(destination);previous=destination
        self.assertEqual(len(set(seen)),8)

    def test_all_anchor_disks_respected_by_every_connector(self):
        targets=[{'key':str(i),'x':x,'y':y} for i,(x,y) in enumerate([(80000,147300),(80600,147800),(81000,146800)])]
        plan=price_route(DATA,(79500,146500),targets)
        self.assertEqual(len(plan['anchors']),3)
        nav=Navigation(DATA,clearance=24)
        for t in plan['blockers']:nav.add_blocker(Point(t['x'],t['y']).buffer(45))
        self.assertTrue(all(nav.clear(a,b) for a,b in zip(plan['points'],plan['points'][1:])))
        line=LineString(plan['points'])
        for t in plan['anchors']:
            nearest=line.distance(Point(t['x'],t['y']))
            self.assertGreaterEqual(nearest,54)
            self.assertLess(nearest,85)

    def test_open_market_passes_continue_forward_without_shop_circles(self):
        data={**DATA,'obstacles':[]}
        targets=[{'key':str(i),'x':80000+500*i,'y':147500} for i in range(3)]
        plan=price_route(data,(79500,147500),targets)
        self.assertEqual(len(plan['anchors']),3)
        line=LineString(plan['points'])
        self.assertLess(line.length,1800)  # 1,500 direct plus safe offsets.
        for target in targets:
            self.assertGreaterEqual(line.distance(Point(target['x'],target['y'])),55)
            self.assertLessEqual(line.distance(Point(target['x'],target['y'])),85)

    def test_impossible_shop_is_deferred_without_aborting_tour(self):
        plan=price_route(DATA,(79500,146500),[{'key':'blocked','x':80250,'y':147000},{'key':'ok','x':80800,'y':147800}])
        self.assertEqual([t['key'] for t in plan['anchors']],['ok'])
        self.assertIn('blocked',[t['key'] for t in plan['deferred']])

if __name__=='__main__':unittest.main()
