import unittest
from unittest.mock import Mock
from types import SimpleNamespace
from walk_guard import WalkGuard


class WallDepartureTests(unittest.TestCase):
    def guard(self,penetration=.9):
        guard=object.__new__(WalkGuard);guard.queries=0
        guard.last_hit={'blocked':True,'time':0,'penetration':penetration,'normal':[1,0,0]}
        guard.probe=SimpleNamespace(trace=Mock(return_value={'blocked':False}))
        return guard

    def test_observed_shallow_wall_overlap_allows_checked_outward_departure(self):
        guard=self.guard()
        self.assertTrue(guard.clear_outward_escape((83591,147199,-3409),(83660,147180)))
        self.assertEqual(guard.probe.trace.call_count,1)

    def test_inward_deep_long_or_still_blocked_departures_remain_forbidden(self):
        for penetration,goal in ((.9,(83520,147199)),(3,(83660,147199)),(.9,(83800,147199))):
            guard=self.guard(penetration)
            self.assertFalse(guard.clear_outward_escape((83591,147199,-3409),goal))
            guard.probe.trace.assert_not_called()
        guard=self.guard();guard.probe.trace.return_value={'blocked':True}
        self.assertFalse(guard.clear_outward_escape((83591,147199,-3409),(83660,147199)))
