import struct
import threading
import unittest
from types import SimpleNamespace
from unittest.mock import Mock
from unittest.mock import patch

from walk_shop_policy import eligible, generation, shop_rows, trader_key, server_nearby, choose_candidate
from walk_shops import WalkShops
from shop_wire import decode_sell_shop, decode_buy_shop
from walk_client import WalkClient
from fast_headless_shop_sweep import decode_record, DATA_SIZE
from walk_shop_wire import ShopWire


def trader(**changes):
    return {'actor':'0x12340','name':'Alice','object_id':42,'kiosk_type':1,
            'x':20.,'y':0.,'z':0.,**changes}


class ShopTests(unittest.TestCase):
    def test_route_anchor_is_not_starved_by_a_closer_neighbor(self):
        anchor=trader(name='anchor',x=75)
        neighbor=trader(name='neighbor',x=20)
        self.assertIs(choose_candidate([neighbor,anchor],(0,0,0),(100,0),95,{'anchor'}),anchor)

    def test_departing_shop_gets_read_before_an_approaching_neighbor(self):
        departing=trader(name='departing',x=-80)
        approaching=trader(name='approaching',x=20)
        self.assertIs(choose_candidate([approaching,departing],(0,0,0),(100,0),95),departing)

    def test_fast_capture_exposes_buy_parser_diagnostics(self):
        raw=bytearray(DATA_SIZE)
        struct.pack_into('<Q',raw,8,1)
        struct.pack_into('<Q',raw,0xB8,0x12345678)
        struct.pack_into('<Q',raw,0xC0,0x87654321)
        event=decode_record(raw,{'functions':{}})
        self.assertEqual(event['parser_caller'],'0x12345678')
        self.assertEqual(event['alternate_converter_caller'],'0x87654321')

    def test_current_range_and_generation_are_required_for_second_action(self):
        old=trader()
        self.assertTrue(eligible(old,old,(0,0,0),85))
        for current in (trader(x=86),trader(object_id=43),trader(name='Bob'),
                        trader(kiosk_type=3),trader(actor='0x56780'),trader(z=61)):
            self.assertFalse(eligible(old,current,(0,0,0),85))
        self.assertEqual(generation(trader(name=' ALICE ')),generation(old))

    def reader(self):
        r=WalkShops.__new__(WalkShops)
        r.radius=85;r.retry={};r.pending={};r.allowed={};r.pause_queue=[]
        r.requested_keys=set();r.unavailable_keys=set();r.dynamic_targets={}
        r.stats={'requests':0,'range_skips':0}
        r.active=threading.Event();r.active.set();r.halt=threading.Event()
        r.walk=SimpleNamespace(cancelled=lambda:False)
        r.hooks=SimpleNamespace(send=Mock(return_value={'send_result':19}))
        r.server_position=(0,0,0)
        import time
        r.server_at=time.monotonic()
        return r

    def test_first_action_reserves_a_frame_instead_of_sending_a_half_pair_at_the_edge(self):
        r=self.reader();r.radius=95;r.collection_zone=None
        r.fresh=Mock(return_value=trader(x=93));r.position=Mock(return_value=(0,0,0))
        r.send_pair(None,trader(x=93),lambda _:None)
        self.assertEqual(r.hooks.send.call_count,0)
        self.assertEqual(r.pending,{})

    def test_second_action_keeps_full_radius_after_reserved_first_action(self):
        r=self.reader();r.radius=95;r.collection_zone=None
        r.fresh=Mock(side_effect=[trader(x=87),trader(x=93)])
        r.position=Mock(return_value=(0,0,0))
        r.send_pair(None,trader(x=87),lambda _:None)
        self.assertEqual(r.hooks.send.call_count,2)

    def test_request_freezes_verification_revision_before_actions(self):
        r=self.reader();r.collection_zone=None
        target={'key':'alice','verification_revision':7}
        r.tracked_targets=[target]
        r.fresh=Mock(return_value=trader());r.position=Mock(return_value=(0,0,0))
        r.send_pair(None,trader(),lambda _:None)
        target['verification_revision']=8
        self.assertEqual(r.pending[42]['verification_revision'],7)
        self.assertIsNotNone(r.pending[42]['read_started_at'])

    def test_client_only_range_is_insufficient(self):
        self.assertTrue(eligible(trader(),trader(),(0,0,0),85))
        self.assertFalse(server_nearby(trader(),(-100,0,0),.1,85))
        self.assertFalse(server_nearby(trader(),(0,0,0),1.0,85))
        self.assertFalse(server_nearby(trader(),None,0,85))
        self.assertTrue(server_nearby(trader(),(0,0,0),.3,85))

    def test_short_pass_accepts_only_bounded_server_position_lag(self):
        shop=trader(x=100,y=0)
        # Live pawn is safely inside the shop radius, while the last server
        # packet trails ordinary movement by about 24 units.
        self.assertTrue(server_nearby(shop,(204,0,0),.22,95,(180,0,0)))
        self.assertFalse(server_nearby(shop,(204,0,0),.22,95,(193,0,0)))
        self.assertFalse(server_nearby(shop,(204,0,0),.01,95,(180,0,0)))
        self.assertFalse(server_nearby(shop,(204,0,0),.81,95,(180,0,0)))

    def test_stopped_read_can_use_older_origin_only_if_both_positions_are_near(self):
        shop=trader(x=84259,y=148000,z=-3406)
        server=(84254,148088,-3400);local=(84256,148063,-3402)
        self.assertFalse(server_nearby(shop,server,1.87,95,local))
        self.assertTrue(server_nearby(shop,server,1.87,95,local,stationary_probe=True))
        self.assertFalse(server_nearby(shop,server,8.01,95,local,stationary_probe=True))
        self.assertFalse(server_nearby(shop,(84254,148100,-3400),2,95,local,stationary_probe=True))
        self.assertFalse(server_nearby(shop,server,2,95,(84256,148045,-3402),stationary_probe=True))
        self.assertFalse(server_nearby(shop,server,2,95,(84256,148063,-3430),stationary_probe=True))

    def test_shop_actions_are_immediately_followed_by_ordinary_movement(self):
        order=[]
        c=WalkClient.__new__(WalkClient)
        c.shops=SimpleNamespace(before_move=lambda:order.append('shop_pair'))
        c.cancelled=lambda:False
        c.position=lambda:(0,0,0)
        c.world={'controller':123};c.functions={'move':456}
        with patch('walk_client.invoke',side_effect=lambda *args:order.append('move')):
            c.move((100,0))
        self.assertEqual(order,['shop_pair','move'])

    def test_cancellation_follows_even_a_partial_shop_pair(self):
        r=self.reader();r.candidate=trader();r.done=set();r.next_pair=0;r.lock=threading.Lock()
        r.stats['target_cancels']=0;r.walk.pid=123;r.log=Mock()
        order=[]
        def partial(*_):
            r.stats['requests']+=1;order.append('first_target')
        r.send_pair=partial
        r.hooks.cancel_target=Mock(side_effect=lambda:order.append('server_cancel') or {})
        r.wire=SimpleNamespace(cancel_replies=lambda _:0)
        with patch('walk_shops.Lu4MemoryClient'),patch('walk_shops.Memory'):
            r.before_move()
        self.assertEqual(order,['first_target','server_cancel'])
        self.assertEqual(r.stats['target_cancels'],1)
        self.assertIn(0,r.allowed)

    def test_new_radar_goal_is_not_delayed_but_next_pair_waits_for_capture(self):
        r=self.reader();r.candidate=trader();r.done=set();r.next_pair=0;r.lock=threading.Lock()
        r.pause_queue=[]
        r.stats['target_cancels']=0;r.walk.pid=123;r.log=Mock()
        r.send_pair=lambda *_:r.stats.__setitem__('requests',r.stats['requests']+1)
        r.hooks.cancel_target=Mock(return_value={})
        r.wire=SimpleNamespace(cancel_replies=lambda _:0)
        with patch('walk_shops.Lu4MemoryClient'),patch('walk_shops.Memory'):
            r.before_move()
            r.next_pair=0;r.candidate=trader(name='Radar',object_id=43)
            r.pending[42]={}
            r.before_move()
            self.assertEqual(r.stats['requests'],1)
            r.pending.clear()
            r.candidate=trader(name='Radar',object_id=43)
            r.before_move()
        self.assertEqual(r.stats['requests'],2)

    def test_server_cancel_reply_requests_one_immediate_route_update(self):
        r=self.reader();r.await_cancel=2;r.walk.client=object()
        r.wire=SimpleNamespace(cancel_replies=Mock(side_effect=[1,2]))
        self.assertFalse(r.needs_resume())
        self.assertTrue(r.needs_resume())
        self.assertFalse(r.needs_resume())

    def test_closed_candidate_does_not_send_a_shop_pair(self):
        r=self.reader();r.candidate=trader();r.done=set();r.next_pair=0;r.lock=threading.Lock()
        r.unavailable_keys.add('alice');r.send_pair=Mock()
        r.before_move()
        r.send_pair.assert_not_called()
        self.assertIsNone(r.candidate)

    def test_pair_does_not_send_second_action_after_exiting_radius(self):
        r=self.reader()
        r.collection_zone=None
        r.position=Mock(side_effect=[(0,0,0),(110,0,0)])
        r.fresh=Mock(return_value=trader())
        r.send_pair(None,trader(),lambda _:None)
        self.assertEqual(r.hooks.send.call_count,1)
        self.assertEqual(r.stats['range_skips'],1)

    def test_live_shop_outside_zone_is_not_requested_even_in_read_radius(self):
        r=self.reader();r.collection_zone={'polygon':[[-10,-10],[10,-10],[10,10],[-10,10]]}
        r.position=Mock(return_value=(0,0,0));r.fresh=Mock(return_value=trader(x=40,y=0))
        r.send_pair(None,trader(x=40,y=0),lambda _:None)
        self.assertEqual(r.hooks.send.call_count,0)

    def test_pair_stops_when_movement_is_paused(self):
        r=self.reader();r.position=Mock(return_value=(0,0,0));r.fresh=Mock(return_value=trader())
        r.collection_zone=None
        def send(*_):
            r.active.clear()
            return {'send_result':19}
        r.hooks.send.side_effect=send
        r.send_pair(None,trader(),lambda _:None)
        self.assertEqual(r.hooks.send.call_count,1)

    def test_own_selected_target_survives_a_long_gap_between_shops(self):
        r=self.reader();r.last_selected=123
        r.allowed[456]=float('inf')
        self.assertTrue(r.allowed_target(456))
        r.allowed[456]=0
        self.assertTrue(r.allowed_target(456))
        self.assertFalse(r.allowed_target(789))

    def test_prices_beyond_int32_are_preserved_and_matched_to_event(self):
        raw=bytearray(105);raw[0]=0xa1
        struct.pack_into('<i',raw,1,42);struct.pack_into('<I',raw,17,1)
        struct.pack_into('<ii',raw,21,55,88)
        struct.pack_into('<q',raw,33,3000000000)
        struct.pack_into('<H',raw,51,7)
        struct.pack_into('<q',raw,89,9000000000)
        wire=decode_sell_shop(bytes(raw))
        event={'side':'sell','count':1,'copied_count':1,
               'rows':[{'item_object_id':55,'item_id':88,'enchant_level':7,'price':410065408}]}
        rows,precision=shop_rows(event,wire,trader())
        self.assertEqual(rows[0]['price'],9000000000)
        self.assertEqual(rows[0]['quantity'],3000000000)
        self.assertEqual(precision,'wire_int64')
        with self.assertRaises(ValueError): shop_rows(event,{**wire,'object_id':43},trader())
        with self.assertRaises(ValueError): decode_sell_shop(bytes(raw[:-1]))

    def test_other_layout_is_explicitly_unverified(self):
        event={'side':'buy','count':1,'copied_count':1,'rows':[{'item_id':57}]}
        self.assertEqual(shop_rows(event,None,trader(kiosk_type=3)),(event['rows'], 'client_int32_unverified'))
        with self.assertRaises(ValueError): shop_rows({**event,'count':2},None,trader(kiosk_type=3))

    def test_only_durable_exact_capture_schedules_trader_pause(self):
        import time
        r=self.reader();r.wire=SimpleNamespace(read=lambda _:[],observed=[])
        r.state={};r.sequence=0;r.done=set();r.captured_keys=set()
        r.stats.update({'invalid_replies':0,'captured_shops':0,'rows':0,
                        'exact_shops':0,'unverified_shops':0,'timeouts':0})
        sell={'side':'sell','count':1,'copied_count':1,'sequence':1,
              'function_name':'PlayerShopSellItemsList',
              'rows':[{'item_id':88,'item_object_id':55,'enchant_level':7}]}
        wire={'side':'sell','object_id':42,'row_count':1,
              'rows':[{'item_id':88,'item_object_id':55,'enchant':7,'price':100,'quantity':2}]}
        r.pending[42]={'capture':sell,'wire':wire,'trader':trader(),
                       'sent':time.monotonic()-1,'actions':[]}
        with patch('walk_shops.read_history',return_value=[]):
            r.consume(object(),lambda _:None)
        self.assertEqual(r.pause_queue,['alice'])
        self.assertEqual(r.captured_keys,{'alice'})
        buy={'side':'buy','count':1,'copied_count':1,'sequence':2,
             'function_name':'PlayerShopBuyItemsList','rows':[{'item_id':89}]}
        r.pending[43]={'capture':buy,'wire':None,'trader':trader(name='Bob',object_id=43,kiosk_type=3),
                       'sent':time.monotonic()-1,'actions':[]}
        with patch('walk_shops.read_history',return_value=[]):
            r.consume(object(),lambda _:None)
        self.assertEqual(r.pause_queue,['alice'])
        self.assertNotIn('bob',r.captured_keys)

    def test_empty_reply_cannot_be_a_successful_price_verification(self):
        event={'side':'sell','count':0,'copied_count':0,'rows':[]}
        wire={'side':'sell','object_id':42,'row_count':0,'rows':[]}
        with self.assertRaisesRegex(ValueError,'empty shop reply'):
            shop_rows(event,wire,trader())

    def test_be_buy_wire_preserves_full_prices_and_checks_event_identity(self):
        raw=bytearray(17+96)
        raw[0]=0xbe
        struct.pack_into('<i',raw,1,42)
        struct.pack_into('<I',raw,13,1)
        struct.pack_into('<ii',raw,17,4021,4037)
        struct.pack_into('<q',raw,17+72,9000000000)
        struct.pack_into('<q',raw,17+88,3000000000)
        wire=decode_buy_shop(bytes(raw))
        event={'side':'buy','count':1,'copied_count':1,'rows':[
            {'item_object_id':1,'item_id':4037,'enchant_level':7,'base_price':0,
             'price':9000000000 & 0xffffffff,'buy_count':3000000000 & 0xffffffff}]}
        rows,precision=shop_rows(event,wire,trader(kiosk_type=3))
        self.assertEqual((rows[0]['price'],rows[0]['quantity'],rows[0]['enchant'],precision),
                         (9000000000,3000000000,7,'wire_int64'))
        with self.assertRaises(ValueError): shop_rows(event,{**wire,'object_id':43},trader(kiosk_type=3))
        with self.assertRaises(ValueError): decode_buy_shop(bytes(raw[:-1]))

        observer=ShopWire.__new__(ShopWire)
        observer.walk=SimpleNamespace(pid=1)
        observer.hook={}
        observer.after=0
        with patch('walk_shop_wire.packets.snapshot',return_value=([{'hex':raw.hex(),'length':len(raw)}],1,0)):
            self.assertEqual(observer.read(object())[0]['rows'][0]['price'],9000000000)
        self.assertEqual(observer.observed,[])


if __name__=='__main__': unittest.main()
