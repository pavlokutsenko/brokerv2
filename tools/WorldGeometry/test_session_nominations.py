import time
import threading
import unittest
from types import SimpleNamespace
from walk_session_bindings import continuation_nominations,needs_unbound_scan,bind_native_session_targets,session_candidate_matches
from cycle_revisit import add_live_radar_targets,unavailable_after_approach


def fixture():
    return SimpleNamespace(walk=SimpleNamespace(pid=20),tracked_targets=[],dynamic_targets={},dynamic_seen_live={},
        captured_keys=set(),unavailable_keys=set(),collection_zone=None,allowed_keys=set(),allowed_object_ids=set(),
        closed_stamps={},lock=threading.Lock(),candidate=None,unbound_absence_scans={},unbound_scan_at=0,
        error=None,live_targets={},requested_keys=set(),absence_scans={'shop':10},last_actor_scan_at=time.monotonic())


class SessionNominationTests(unittest.TestCase):
    def nomination(self,shops,pid=20):
        continuation_nominations(shops,{'pid':pid,'traders':[{'name':'Shop','object_id':0,'kiosk_type':1,'x':100,'y':100}]},lambda _:None)

    def test_carried_destination_is_approached_once_but_cannot_authorize_rpc(self):
        shops=fixture();self.nomination(shops,10)
        self.assertEqual(shops.dynamic_targets,{})
        self.nomination(shops)
        pending={}
        self.assertEqual(add_live_radar_targets(pending,shops,set(),(0,0),time.monotonic()),['shop'])
        self.assertEqual(add_live_radar_targets(pending,shops,{'shop'},(0,0),time.monotonic()),[])
        actor={'name':'Shop','object_id':99,'kiosk_type':1,'x':100,'y':100}
        self.assertFalse(session_candidate_matches(shops,actor))
        self.assertFalse(needs_unbound_scan(shops,(0,0,0)))
        self.assertTrue(needs_unbound_scan(shops,(100,100,0)))
        shops.fresh=lambda memory,row:row
        bind_native_session_targets(shops,None,[actor],(100,100,0),lambda _:None)
        self.assertTrue(session_candidate_matches(shops,actor))
        self.assertFalse(session_candidate_matches(shops,{**actor,'object_id':100}))
        self.assertFalse(session_candidate_matches(shops,{**actor,'kiosk_type':8}))
        self.assertEqual(shops.allowed_object_ids,{99})

    def test_standing_or_changed_shop_skips_current_pass_without_a_request(self):
        for kiosk in (0,8):
            shops=fixture();self.nomination(shops);events=[]
            shops.fresh=lambda memory,row:row
            bind_native_session_targets(shops,None,[{'name':'Shop','object_id':99,'kiosk_type':kiosk,'x':100,'y':100}],
                                        (100,100,0),events.append)
            self.assertEqual(shops.unavailable_keys,{'shop'})
            self.assertEqual(shops.dynamic_targets,{})
            self.assertEqual(events[-1]['type'],'shop_closed')

    def test_only_three_fresh_complete_nearby_scans_can_prove_local_absence(self):
        shops=fixture();self.nomination(shops)
        target=shops.dynamic_targets['shop'];shops.fresh=lambda *_:None
        for index in range(3):
            bind_native_session_targets(shops,None,[],(100,100,0),lambda _:None)
            self.assertEqual(unavailable_after_approach(shops,target,(100,100,0),time.monotonic()),index==2)
        self.assertFalse(unavailable_after_approach(shops,target,(100,100,0),time.monotonic()+2))
        self.assertFalse(unavailable_after_approach(shops,target,(201,100,0),time.monotonic()))


if __name__=='__main__': unittest.main()
