"""A late moving-origin correction retains map, capsule and stop guards."""
import time
import unittest
from types import SimpleNamespace
from unittest.mock import Mock, patch
from walk_handoff import reconnect_handoff
from walk_follow import follow


class HandoffOriginTests(unittest.TestCase):
    def client(self, age=.7, displacement=100):
        return SimpleNamespace(last_handoff_at=time.monotonic()-age,
                               last_handoff_position=(100-displacement, 0, 0))

    def navigation(self):
        return SimpleNamespace(shortest=lambda a, b: [a, b], clear=lambda *_: True)

    def test_late_handoff_preserves_every_original_point(self):
        points=[(0, 0), (-100, 0), (-200, 0)]
        logs=[]
        result=reconnect_handoff(self.client(), self.navigation(), points, (100,0,0), logs.append)
        self.assertEqual(result[0], (100,0))
        self.assertEqual(result[-len(points):], points)
        self.assertEqual(logs[0]['type'], 'handoff_origin_reconnected')

    def test_no_witness_stale_witness_jump_and_long_drift_are_not_rebased(self):
        points=[(0,0),(-200,0)]
        for client, current in ((SimpleNamespace(),(100,0,0)),
                (self.client(age=3),(100,0,0)),
                (self.client(age=.1,displacement=200),(100,0,0)),
                (self.client(age=2,displacement=300),(300,0,0))):
            log=Mock()
            self.assertIs(reconnect_handoff(client,self.navigation(),points,current,log),points)
            log.assert_not_called()

    def test_blocked_connector_or_large_mapped_detour_does_not_change_path(self):
        points=[(0,0),(-200,0)]
        for nav in (SimpleNamespace(shortest=lambda *_:[(100,0),(0,0)],clear=lambda *_:False),
                SimpleNamespace(shortest=lambda *_:[(100,0),(100,1000),(0,0)],clear=lambda *_:True)):
            self.assertIs(reconnect_handoff(self.client(),nav,points,(100,0,0),Mock()),points)

    def test_follower_still_checks_capsule_and_cancel_before_movement(self):
        for cancelled in (True,False):
            client=self.client()
            client.position=lambda:(100,0,0)
            client.shops=None;client.stop=Mock();client.move=Mock()
            client.cancelled=lambda:cancelled
            client.world={'controller':0};client.initial_selected=0
            client.m=SimpleNamespace(u64=lambda _:0)
            guard=SimpleNamespace(clear=Mock(return_value=False),last_hit=None)
            with patch('walk_follow.time.sleep'):
                result=follow(client,self.navigation(),[(0,0),(-200,0)],lambda _:None,5,guard=guard)
            self.assertEqual(result['reason'],'cancelled' if cancelled else 'blocked')
            client.move.assert_not_called()
            self.assertIsNone(client.last_handoff_at)


if __name__=='__main__':unittest.main()
