import json
import tempfile
import threading
import unittest
from datetime import datetime,timezone,timedelta
from pathlib import Path
from types import SimpleNamespace
from walk_shop_lifecycle import close_visit,native_closed,native_closed_observations
from walk_shops import WalkShops
from walk_follow import follow


def fixture():
    target={'key':'new','name':'New','object_id':12,'kiosk_type':1,'actor':'0x1234','x':100,'y':0}
    return SimpleNamespace(dynamic_targets={'new':target},dynamic_seen_live={'new':target},
        tracked_targets=[],unavailable_keys=set(),captured_keys=set(),closed_stamps={},
        allowed_keys={'new'},allowed_object_ids={12},candidate=target,lock=threading.Lock(),
        requested_keys=set(),dynamic_detour_attempted=set(),done=set(),retry={},reopened_keys=set()),target


class LifecycleTests(unittest.TestCase):
    def test_new_candidate_already_standing_cancels_without_previous_native_shop(self):
        shops,target=fixture();events=[];shops.dynamic_seen_live={}
        actor={**target,'kiosk_type':0}
        shops.fresh=lambda *args:None
        native_closed_observations(shops,None,[actor],events.append)
        self.assertEqual(events,[])  # Failed class/root validation isn't closure.
        shops.fresh=lambda *args:{**actor,'name':'SomeoneElse'}
        native_closed_observations(shops,None,[actor],events.append)
        self.assertEqual(events,[])
        shops.fresh=lambda *args:actor
        native_closed_observations(shops,None,[{**actor,'object_id':13}],events.append)
        self.assertEqual(events,[])
        native_closed_observations(shops,None,[actor],events.append)
        self.assertEqual(shops.dynamic_targets,{})
        self.assertEqual(shops.unavailable_keys,{'new'})
        self.assertIsNone(shops.candidate)
        self.assertEqual(events[0]['source'],'native kiosk=0')

    def test_positive_closure_cancels_only_matching_generation_once(self):
        shops,target=fixture();events=[]
        shops.pending={12:{'capture':'old generation'},13:{'capture':'other trader'}}
        self.assertFalse(close_visit(shops,'new',13,events.append,'packet'))
        self.assertTrue(close_visit(shops,'new',12,events.append,'packet'))
        self.assertEqual(shops.unavailable_keys,{'new'})
        self.assertEqual(shops.dynamic_targets,{})
        self.assertIsNone(shops.candidate)
        self.assertNotIn(12,shops.pending)
        self.assertIn(13,shops.pending)
        self.assertFalse(close_visit(shops,'new',12,events.append,'packet'))
        self.assertEqual(len(events),1)

    def test_native_absence_is_not_closure_but_matching_zero_kiosk_is(self):
        shops,target=fixture();events=[]
        shops.fresh=lambda *args:None
        native_closed(shops,None,{'new':target},{},events.append)
        self.assertEqual(events,[])
        shops.fresh=lambda *args:{**target,'kiosk_type':0,'object_id':13}
        native_closed(shops,None,{'new':target},{},events.append)
        self.assertEqual(events,[])
        shops.fresh=lambda *args:{**target,'kiosk_type':0}
        native_closed(shops,None,{'new':target},{},events.append)
        self.assertEqual(events[0]['source'],'native kiosk=0')

    def test_old_packet_cannot_requeue_native_closed_shop_but_reopen_can(self):
        shops,target=fixture();events=[];now=datetime.now(timezone.utc)
        close_visit(shops,'new',12,events.append,'native')
        shops.broker_keys=set();shops.collection_zone=None
        with tempfile.TemporaryDirectory() as folder:
            shops.radar_file=Path(folder)/'radar.json'
            def frame(seen):
                shops.radar_file.write_text(json.dumps({'at':datetime.now(timezone.utc).isoformat(),
                    'traders':[{**target,'observed_at':seen.isoformat()}]}))
                WalkShops.refresh_radar(shops,events.append)
            frame(now-timedelta(seconds=1))
            self.assertEqual(shops.dynamic_targets,{})
            frame(now+timedelta(seconds=1))
            self.assertIn('new',shops.dynamic_targets)
            self.assertNotIn('new',shops.unavailable_keys)

    def test_approach_cancels_before_move_without_waiting_at_standing_actor(self):
        shops,target=fixture();shops.active=threading.Event();shops.error=None
        shops.unavailable_keys={'new'}
        moved=[];paused=[];events=[]
        client=SimpleNamespace(shops=shops,read_goal_key='new',pass_goal_key=None,
            position=lambda:(0,0,0),cancelled=lambda:False,move=lambda p:moved.append(p),
            pause_for_plan=lambda:paused.append(True))
        result=follow(client,None,[(0,0),(1000,0)],events.append)
        self.assertEqual(result['reason'],'temporarily_unavailable')
        self.assertEqual(moved,[]);self.assertEqual(paused,[True])

    def test_carried_zero_id_cannot_reopen_native_closed_shop(self):
        shops,target=fixture();events=[];now=datetime.now(timezone.utc)
        shops.walk=SimpleNamespace(pid=20);shops.broker_keys=set();shops.collection_zone=None
        close_visit(shops,'new',12,events.append,'native')
        with tempfile.TemporaryDirectory() as folder:
            shops.radar_file=Path(folder)/'radar.json'
            row={**target,'object_id':0,'observed_at':(now-timedelta(minutes=10)).isoformat()}
            for _ in range(3):
                shops.radar_file.write_text(json.dumps({'pid':20,'at':datetime.now(timezone.utc).isoformat(),
                    'traders':[row]}))
                WalkShops.refresh_radar(shops,events.append)
                self.assertEqual(shops.unavailable_keys,{'new'})
                self.assertIn('new',shops.closed_stamps)
                self.assertEqual(shops.dynamic_targets,{})
            row={**target,'observed_at':(datetime.now(timezone.utc)+timedelta(seconds=1)).isoformat()}
            shops.radar_file.write_text(json.dumps({'pid':20,'at':datetime.now(timezone.utc).isoformat(),
                'traders':[row]}))
            WalkShops.refresh_radar(shops,events.append)
            self.assertIn('new',shops.dynamic_targets)
            self.assertNotIn('new',shops.unavailable_keys)

    def test_same_id_reopen_after_capture_releases_old_visit_suppression(self):
        shops,target=fixture();now=datetime.now(timezone.utc);events=[]
        shops.captured_keys={'new'};shops.done={('new',12,1)};shops.retry={('new',12,1):9999}
        shops.requested_keys={'new'};shops.dynamic_detour_attempted={'new'}
        self.assertTrue(close_visit(shops,'new',12,events.append,'packet',now.isoformat()))
        shops.broker_keys={'new'};shops.collection_zone=None
        with tempfile.TemporaryDirectory() as folder:
            shops.radar_file=Path(folder)/'radar.json'
            shops.radar_file.write_text(json.dumps({'at':now.isoformat(),'traders':[
                {**target,'observed_at':(now+timedelta(seconds=1)).isoformat(),'reopened':True}]}))
            WalkShops.refresh_radar(shops,events.append)
        self.assertIn('new',shops.dynamic_targets)
        self.assertTrue(shops.dynamic_targets['new']['reopened'])
        self.assertEqual(shops.captured_keys,set());self.assertEqual(shops.done,set())
        self.assertEqual(shops.requested_keys,set());self.assertEqual(shops.retry,{})
        self.assertNotIn('new',shops.unavailable_keys)


if __name__=='__main__': unittest.main()
