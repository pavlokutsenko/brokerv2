import math
import threading
import unittest
from types import SimpleNamespace
from shapely.geometry import LineString,Point
from radar_pass_plan import radar_pass_route
from walk_follow import follow

DATA={'origin':[0,0],'extent':[-2000,2000,-2000,2000],'obstacles':[],'unknown':[]}


class RadarPassTests(unittest.TestCase):
    def test_tangent_short_arc_reads_at_standoff_and_leaves_forward(self):
        points,nav=radar_pass_route(DATA,(-900,0),{'x':0,'y':0},onward=(900,0))
        line=LineString(points)
        self.assertGreaterEqual(line.distance(Point(0,0)),55)
        self.assertLessEqual(line.distance(Point(0,0)),72)
        self.assertGreater(points[-1][0],0)
        self.assertGreater(math.dist(points[-1],(0,0)),95)
        self.assertLess(line.length,1200)
        self.assertTrue(all(nav.clear(a,b) for a,b in zip(points,points[1:])))
        arc=points[-14:-1]
        self.assertTrue(all(abs(math.dist(p,(0,0))-72)<1e-6 for p in arc))
        self.assertLess(sum(math.dist(a,b) for a,b in zip(arc,arc[1:])),80)

    def test_blocked_side_chooses_opposite_arc(self):
        data={**DATA,'obstacles':[{'rings':[[[-150,80],[150,80],[150,250],[-150,250]]]}]}
        points,nav=radar_pass_route(data,(-900,0),{'x':0,'y':0})
        self.assertLess(points[-8][1],0)
        self.assertTrue(all(nav.clear(a,b) for a,b in zip(points,points[1:])))

    def test_narrow_corridor_falls_back_instead_of_forcing_circle(self):
        data={**DATA,'obstacles':[{'rings':[[[-500,y],[500,y],[500,y+100],[-500,y+100]]]} for y in (100,-200)]}
        self.assertIsNone(radar_pass_route(data,(-900,0),{'x':0,'y':0}))
        self.assertIsNone(radar_pass_route(DATA,(40,0),{'x':0,'y':0}))

    def test_already_in_read_range_keeps_a_forward_arc(self):
        points,nav=radar_pass_route(DATA,(70,0),{'x':0,'y':0},onward=(0,900))
        self.assertGreaterEqual(LineString(points).distance(Point(0,0)),55)
        self.assertGreater(math.dist(points[-1],(0,0)),95)
        self.assertTrue(all(nav.clear(a,b) for a,b in zip(points,points[1:])))

    def test_completed_arc_capture_hands_off_without_stop_or_stationary_read(self):
        stopped=[]
        shops=SimpleNamespace(active=threading.Event(),captured_keys={'new'})
        client=SimpleNamespace(shops=shops,pass_goal_key='new',read_goal_key=None,
            position=lambda:(0,0,0),stop=lambda:stopped.append(True))
        result=follow(client,None,[(0,0),(0,0)],lambda row:None)
        self.assertTrue(result['handoff'])
        self.assertEqual(stopped,[])


if __name__=='__main__': unittest.main()
