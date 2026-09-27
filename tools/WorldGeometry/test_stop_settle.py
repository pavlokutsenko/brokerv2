import unittest
from types import SimpleNamespace
from unittest.mock import patch
from walk_client import WalkClient


class StopSettleTests(unittest.TestCase):
    def client(self,position):
        c=object.__new__(WalkClient)
        c.owned=True;c.shops=SimpleNamespace(active=SimpleNamespace(clear=lambda:None),before_move=lambda:None)
        c.world={'controller':1,'player_actor':2};c.functions={'stop':3,'move':4}
        c.position=position;c.cancelled=lambda:False
        return c

    def test_duplicate_stop_reuses_confirmed_rest_but_move_and_force_invalidate_it(self):
        now=[100.]
        def sleep(seconds): now[0]+=seconds
        c=self.client(lambda:(0,0,0))
        with patch('walk_client.time.monotonic',side_effect=lambda:now[0]), \
                patch('walk_client.time.sleep',side_effect=sleep),patch('walk_client.invoke') as invoke:
            c.stop();first=invoke.call_count;settled=now[0]
            self.assertGreaterEqual(settled-100,1.5)
            c.stop();self.assertEqual(invoke.call_count,first);self.assertEqual(now[0],settled)
            c.move((10,0));c.stop();self.assertGreaterEqual(now[0]-settled,1.5)
            settled=now[0];c.stop(force=True);self.assertGreaterEqual(now[0]-settled,1.5)

    def test_external_motion_and_late_move_reply_restart_settling(self):
        now=[100.]
        c=self.client(lambda:((10 if now[0]>=100.6 else 0),0,0))
        c.stopped_position=(-10,0,0);c.stopped_at=100
        with patch('walk_client.time.monotonic',side_effect=lambda:now[0]), \
                patch('walk_client.time.sleep',side_effect=lambda s:now.__setitem__(0,now[0]+s)), \
                patch('walk_client.invoke'):
            c.stop()
        self.assertGreaterEqual(now[0],102.1)
        self.assertEqual(c.stopped_position,(10,0,0))

    def test_planning_pause_sends_one_stop_without_claiming_confirmed_rest(self):
        c=self.client(lambda:(0,0,0));c.stopped_position=(0,0,0)
        with patch('walk_client.invoke') as invoke,patch('walk_client.time.sleep') as sleep:
            c.pause_for_plan()
        self.assertIsNone(c.stopped_position)
        invoke.assert_called_once();sleep.assert_not_called()


if __name__=='__main__': unittest.main()
