import unittest
from shapely.geometry import LineString, Point
from city_maps import load_navigation
from cycle_recheck_plan import recheck_route
from walk_geometry import Navigation, rounded_route
from walk_escape import escape


class HardExclusionTests(unittest.TestCase):
    def setUp(self):
        self.data={**load_navigation('Giran'),'obstacles':[],'unknown':[],'ground':None}

    def test_cart_remains_blocked_without_projected_geometry(self):
        nav=Navigation(self.data,clearance=0)
        for point in ((81865.72,149588.77),(82150.97,149787.19),(82008,149688)):
            self.assertFalse(nav.clear(point,point))
        self.assertFalse(nav.clear((81750,149688),(82300,149688)))
        self.assertFalse(nav.clear_forbidden((81750,149688),(82300,149688)))
        points=rounded_route(nav.shortest((81750,149688),(82300,149688)),nav)
        self.assertFalse(nav.forbidden.intersects(LineString(points)))

    def test_fallback_recheck_detours_confirmed_trap(self):
        # Full survey says the whole target room is occupied: force fallback.
        data={**self.data,'unknown':[{'rings':[[[1700,2400],[2400,2400],
            [2400,3100],[1700,3100]]]}]}
        points,nav,direct=recheck_route(data,(81750,149688),{'x':82300,'y':149688})
        self.assertTrue(direct)
        self.assertTrue(points)
        self.assertFalse(nav.forbidden.intersects(LineString(points)))
        self.assertLessEqual(Point(points[-1]).distance(Point(82300,149688)),69)

    def test_escape_never_sends_move_inside_confirmed_trap(self):
        class Client:
            def position(self): return (82008,149688,-3434)
            def cancelled(self): return False
            def move(self,goal): raise AssertionError('move into/from hard trap')
        class Guard:
            def clear(self,*args): raise AssertionError('hard exclusion must precede native probe')
        import time
        self.assertFalse(escape(Client(),Navigation(self.data,clearance=0),Guard(),
                                lambda row:None,time.monotonic()+3))

    def test_departure_can_leave_planning_margin_outside_actual_exclusion(self):
        nav=Navigation(self.data,clearance=24)
        source=(82189,149685)
        self.assertFalse(nav.clear(source,source))
        self.assertTrue(nav.clear_forbidden(source,(82260,149685)))
        self.assertFalse(nav.clear_forbidden(source,(82008,149685)))


if __name__=='__main__': unittest.main()
