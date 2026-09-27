import unittest
from test_shop_lifecycle import fixture
from walk_radar_targets import apply_radar_target


class RadarRevisionTests(unittest.TestCase):
    def test_fast_reopen_replaces_already_read_dynamic_target_once(self):
        shops,target=fixture();shops.broker_keys={'new'};shops.collection_zone=None
        target['verification_revision']='old'
        shops.captured_keys={'new'};shops.requested_keys={'new'}
        shops.done={('new',12,1)}
        shops.pending={12:{'trader':target,'verification_revision':'old'}}
        changed={**target,'verification_revision':'new','reopened':True}
        events=[];apply_radar_target(shops,changed,events.append)
        self.assertEqual(shops.dynamic_targets['new']['verification_revision'],'new')
        self.assertEqual(shops.pending,{})
        self.assertEqual(shops.captured_keys,set())
        self.assertEqual(shops.done,set())
        shops.captured_keys.add('new')
        apply_radar_target(shops,changed,events.append)
        self.assertEqual(shops.captured_keys,{'new'})
        self.assertEqual(len(events),1)

    def test_changed_revision_of_assigned_shop_is_not_blocked_by_broker_membership(self):
        shops,target=fixture();target['verification_revision']='old'
        shops.dynamic_targets={};shops.tracked_targets=[target];shops.broker_keys={'new'}
        shops.collection_zone=None
        apply_radar_target(shops,{**target,'verification_revision':'new','object_id':14},lambda _:None)
        self.assertEqual(shops.dynamic_targets['new']['object_id'],14)
        self.assertEqual(shops.dynamic_targets['new']['verification_revision'],'new')
