import threading
import time
import unittest
from collections import deque
from types import SimpleNamespace

from walk_trader_pause import wait_after_capture


class TraderPauseTests(unittest.TestCase):
    def shops(self,seconds,keys):
        active=threading.Event();active.set()
        return SimpleNamespace(active=active,lock=threading.Lock(),pause_queue=deque(keys),
            trader_pause_seconds=seconds,pause_seconds_spent=0.0,error=None,
            stats={'captured_shops':len(keys),'exact_shops':len(keys)})

    def test_exact_capture_stops_and_waits_before_next_move(self):
        shops=self.shops(.02,['first','second'])
        order=[];progress=[]
        client=SimpleNamespace(cancelled=lambda:False,
            pause_for_plan=lambda:order.append('stop') or shops.active.clear())
        status,elapsed=wait_after_capture(shops,client,order.append,progress.append)
        order.append('next_move')
        self.assertEqual(status,'ready')
        self.assertGreaterEqual(elapsed,.04)
        self.assertEqual([v for v in order if isinstance(v,str)],['stop','stop','next_move'])
        self.assertEqual([v['key'] for v in order if isinstance(v,dict)],['first','second'])
        self.assertTrue(shops.active.is_set())
        self.assertAlmostEqual(shops.pause_seconds_spent,elapsed,delta=.01)
        self.assertTrue(progress)

    def test_zero_pause_does_not_stop_movement(self):
        shops=self.shops(0,['first'])
        client=SimpleNamespace(cancelled=lambda:False,pause_for_plan=lambda:self.fail('unexpected stop'))
        self.assertEqual(wait_after_capture(shops,client,lambda _:None),('ready',0.0))
        self.assertFalse(shops.pause_queue)

    def test_user_stop_interrupts_wait(self):
        shops=self.shops(2,['first'])
        stopped=threading.Event()
        client=SimpleNamespace(cancelled=stopped.is_set,
            pause_for_plan=lambda:shops.active.clear())
        timer=threading.Timer(.03,stopped.set);timer.start()
        try: status,elapsed=wait_after_capture(shops,client,lambda _:None)
        finally: timer.join()
        self.assertEqual(status,'cancelled')
        self.assertLess(elapsed,1)
        self.assertFalse(shops.active.is_set())


if __name__=='__main__':unittest.main()
