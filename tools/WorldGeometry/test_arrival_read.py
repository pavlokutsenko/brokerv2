import threading
import unittest
from types import SimpleNamespace
from unittest.mock import patch
from walk_follow import follow


class ArrivalReadTests(unittest.TestCase):
    def test_arrival_request_and_exact_reply_handoff_without_stop_settling(self):
        order=[]
        shops=SimpleNamespace(active=threading.Event(),captured_keys=set(),error=None,
            stats={'requests':0},lock=threading.Lock(),retry={},read_hold_key=None,
            allowed_target=lambda _:True)
        def move(point):
            self.assertEqual(point,(0,0));self.assertTrue(shops.active.is_set())
            self.assertEqual(shops.read_hold_key,'shop')
            order.append('request');shops.stats['requests']+=2;shops.captured_keys.add('shop')
        client=SimpleNamespace(shops=shops,read_goal_key='shop',position=lambda:(0,0,0),
            world={'controller':1},initial_selected=0,m=SimpleNamespace(u64=lambda _:0),
            cancelled=lambda:False,move=move,stop=lambda:order.append('stop'))
        logs=[]
        with patch('walk_follow.time.sleep'),patch('walk_arrival_read.time.sleep'):
            result=follow(client,None,[(0,0),(0,0)],logs.append)
        self.assertEqual(order,['request'])
        self.assertTrue(result['handoff'])
        self.assertEqual(result['reason'],'completed')
        self.assertIsNone(shops.read_hold_key)
        self.assertEqual(next(r for r in logs if r['type']=='arrival_read')['requests'],2)


if __name__=='__main__': unittest.main()
