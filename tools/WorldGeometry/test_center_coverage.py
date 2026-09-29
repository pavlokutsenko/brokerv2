import json
import math
import random
import unittest
from pathlib import Path
from calculate_center import calculate_center
from city_maps import load_navigation
from cycle_plan import center_route,price_navigation_data
from walk_geometry import Navigation


class CenterCoverageTests(unittest.TestCase):
    def test_giran_all_standing_points_cover_the_entire_polygon(self):
        polygon=json.loads((Path(__file__).resolve().parents[2]/'maps/Giran/city.json').read_text())['collectionZone']['polygon']
        result=calculate_center(polygon)
        self.assertEqual(result['center'],[82710,148425])
        self.assertTrue(result['coversZone'])
        self.assertAlmostEqual(result['remainingRange'],298.21563679041265)
        for i in range(36):
            angle=i*math.tau/36
            point=(82710+200*math.cos(angle),148425+200*math.sin(angle))
            self.assertLessEqual(max(math.dist(point,p) for p in polygon),2900)
        self.assertGreater(max(math.dist((82413.61851503256,148116.9785946493),p) for p in polygon)+500,3000)

    def test_calculated_center_is_reachable_with_stopping_margin(self):
        data=price_navigation_data(load_navigation('Giran'));nav=Navigation(data)
        center=(82710,148425)
        self.assertTrue(nav.clear(center,center))
        polygon=json.loads((Path(__file__).resolve().parents[2]/'maps/Giran/city.json').read_text())['collectionZone']['polygon']
        for index in range(8):
            angle=index*math.tau/8
            stop=(center[0]+140*math.cos(angle),center[1]+140*math.sin(angle))
            self.assertTrue(nav.clear(stop,stop))
            self.assertLessEqual(max(math.dist(stop,p) for p in polygon)+25+23,2900)
        previous=None
        visited=set()
        for seed in range(16):
            plan=center_route(data,(82378.645,147941.962),center,previous,random.Random(seed),200)
            self.assertLessEqual(math.dist(center,plan['destination'])+23,200)
            self.assertLessEqual(max(math.dist(plan['destination'],p) for p in polygon)+23,2900)
            self.assertTrue(all(nav.clear(a,b) for a,b in zip(plan['points'],plan['points'][1:])))
            if previous:self.assertGreaterEqual(math.dist(previous,plan['destination']),100)
            visited.add(tuple(round(v) for v in plan['centerStop']))
            previous=plan['destination']
        self.assertGreaterEqual(len(visited),4)


if __name__=='__main__':unittest.main()
