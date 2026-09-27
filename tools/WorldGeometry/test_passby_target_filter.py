import unittest
from unittest.mock import patch

from passby_event_collector import current_traders


class TargetedScanTests(unittest.TestCase):
    def test_full_actor_details_only_for_leased_object_ids(self):
        class Memory:
            def __init__(self, *_):
                pass

            def u64(self, _):
                return 0x30000

            def f64(self, _):
                return 0.0

            def i32(self, address, _default=-1):
                return {0x10000 + 0x550: 42, 0x20000 + 0x550: 77}.get(address, -1)

        visited=[]
        def positioned(_memory, _world, actors):
            visited.extend(actors)
            return [{'actor':hex(actor),'object_id':42,'name':'Alice','kiosk_type':3}
                    for actor in actors]

        snapshot={'pid':1,'persistent_level':'0x40000','player_actor':'0x50000'}
        with (patch('passby_event_collector.Memory', Memory),
              patch('passby_event_collector.coherent_actor_snapshot',return_value=([0x10000,0x20000],1)),
              patch('passby_event_collector.enumerate_positioned_actors',side_effect=positioned)):
            traders,_=current_traders(object(),snapshot,{42})
        self.assertEqual(visited,[0x10000])
        self.assertEqual(traders[0]['name'],'Alice')
        with (patch('passby_event_collector.Memory', Memory),
              patch('passby_event_collector.coherent_actor_snapshot',return_value=([0x10000,0x20000],1)),
              patch('passby_event_collector.enumerate_positioned_actors',return_value=[
                  {'actor':'0x10000','object_id':42,'name':'Alice','kiosk_type':0}])):
            self.assertEqual(current_traders(object(),snapshot,{42})[0],[])
            self.assertEqual(current_traders(object(),snapshot,{42},include_closed=True)[0][0]['kiosk_type'],0)
