"""Actual follower switches a guarded transit when background output appears."""
import threading
import unittest
from types import SimpleNamespace
from unittest.mock import Mock,patch
from walk_follow import follow


class PreparationHandoffTests(unittest.TestCase):
    def test_ready_plan_continues_without_stop_and_cancellation_still_wins(self):
        for cancel in (False,True):
            moved=[];position=[0,0,0];events=[]
            shops=SimpleNamespace(active=threading.Event(),error=None,captured_keys=set(),
                anchor_positions=[],needs_resume=lambda:False)
            def move(goal):moved.append(goal);position[0]=60
            client=SimpleNamespace(shops=shops,position=lambda:tuple(position),move=move,
                stop=Mock(),cancelled=lambda:cancel and bool(moved),preparation_ready=lambda:bool(moved),
                world={'controller':0},initial_selected=0,m=SimpleNamespace(u64=lambda _:0))
            guard=SimpleNamespace(clear=Mock(return_value=True))
            with patch('walk_follow.time.sleep') as sleep:
                result=follow(client,SimpleNamespace(clear=lambda *_:True),[(0,0),(900,0)],events.append,5,guard=guard)
            self.assertEqual(len(moved),1);guard.clear.assert_called_once()
            if cancel:
                self.assertEqual(result['reason'],'cancelled');client.stop.assert_called_once()
                self.assertFalse(result['handoff'])
            else:
                self.assertEqual(result['reason'],'background_plan_ready');self.assertTrue(result['handoff'])
                client.stop.assert_not_called()
                self.assertNotIn(.5,[call.args[0] for call in sleep.call_args_list])


if __name__=='__main__':unittest.main()
