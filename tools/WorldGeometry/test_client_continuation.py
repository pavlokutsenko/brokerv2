import json
import tempfile
import time
import unittest
from datetime import datetime,timezone
from pathlib import Path
from types import SimpleNamespace
from walk_shops import WalkShops
from cycle_revisit import unavailable_after_approach


class ClientContinuationTests(unittest.TestCase):
    def test_new_pid_binding_requires_current_frame_same_name_type_and_location(self):
        with tempfile.TemporaryDirectory() as folder:
            target={'key':'shop','name':'Shop','object_id':0,'kiosk_type':1,'x':100,'y':100,'rebind':True}
            shops=SimpleNamespace(walk=SimpleNamespace(pid=20),tracked_targets=[target],
                radar_file=Path(folder)/'radar.json',allowed_object_ids=set())
            events=[]
            for pid,name,kiosk,x in ((10,'Shop',1,100),(20,'Other',1,100),(20,'Shop',3,100),(20,'Shop',1,120)):
                frame={'at':datetime.now(timezone.utc).isoformat(),'pid':pid,'traders':[],
                       'bindings':[{'name':name,'object_id':99,'kiosk_type':kiosk,'x':x,'y':100}]}
                shops.radar_file.write_text(json.dumps(frame))
                WalkShops.refresh_radar(shops,events.append)
                self.assertEqual(target['object_id'],0)
            frame['bindings'][0].update(name='Shop',kiosk_type=1,x=100)
            shops.radar_file.write_text(json.dumps(frame))
            WalkShops.refresh_radar(shops,events.append)
            self.assertEqual(target['object_id'],99)
            self.assertEqual(shops.allowed_object_ids,{99})
            self.assertEqual(events[0]['type'],'runtime_rebound')

    def test_unbound_scan_does_not_prove_departure(self):
        now=time.monotonic()
        shops=SimpleNamespace(error=None,live_targets={},requested_keys=set(),captured_keys=set(),
                              absence_scans={'shop':100},last_actor_scan_at=now)
        target={'key':'shop','object_id':0,'rebind':True,'x':100,'y':100}
        self.assertFalse(unavailable_after_approach(shops,target,(100,100,0),now))
        target['object_id']=99
        self.assertTrue(unavailable_after_approach(shops,target,(100,100,0),now))


if __name__=='__main__': unittest.main()
