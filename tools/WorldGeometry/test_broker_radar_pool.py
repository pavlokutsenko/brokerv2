"""No game access: exercise packet-only handoff through the real route runner."""
import json
import tempfile
import threading
import unittest
from pathlib import Path
from unittest.mock import patch
import cycle_route
from cycle_revisit import select_revisit_targets


class BrokerPoolTests(unittest.TestCase):
    def test_packet_candidate_can_nominate_approach_before_native_actor_loads(self):
        packet={'key':'new','name':'New','object_id':12,'kiosk_type':8,'x':2500,'y':0}
        candidates=select_revisit_targets([packet],set(),{'new'},(0,0))
        self.assertEqual(candidates,[packet])
        self.assertNotIn('actor',candidates[0])  # Does not authorize reading memory/actions.

    def test_radar_only_route_starts_existing_reader_and_revisits_pool(self):
        instances=[]
        class Client:
            execution_clearance=45
            cleanup_callbacks=[]
            def __init__(self,pid): pass
            def __enter__(self): return self
            def __exit__(self,*args): pass
            def cancelled(self): return False
            def wait_navigation_capsule(self): pass
            def install(self): pass
            def position(self): return (0,0,0)
            def close(self): pass
        class Nav:
            def __init__(self,*args,**kwargs): pass
            def clear(self,*args): return True
        class Shops:
            def __init__(self,client,prefix,*args):
                instances.append(self);self.path=prefix.with_suffix('.shops.jsonl')
                self.active=threading.Event();self.discovery_ready=threading.Event()
                self.captured_keys=set();self.unavailable_keys=set();self.ignored_outside_keys=set()
                self.dynamic_targets={'new':{'name':'New','object_id':12,'x':2500,'y':0}}
                self.error=None;self.started=False
            def prepare(self): pass
            def start(self): self.started=True;self.discovery_ready.set()
            def summary(self): return {'requests':0,'captured_shops':0}
        def revisit(client,shops,*args):
            self.assertTrue(shops.started and shops.active.is_set())
            self.assertEqual(shops.tracked_targets,[])
            return {'reason':'completed','seconds':1,'recoveries':[],
                    'position':(0,0,0),'targets':['new'],'unresolved':[]}
        with tempfile.TemporaryDirectory() as folder:
            input_file=Path(folder)/'input.json';output=Path(folder)/'result.json'
            input_file.write_text(json.dumps({'city':'Giran','mode':'prices','center':[0,0],
                'targets':[],'brokerKeys':['known'],'radarFile':str(Path(folder)/'radar.json')}))
            with patch.multiple(cycle_route,WalkClient=Client,WalkGuard=lambda *args:None,
                    Navigation=Nav,WalkShops=Shops,load_navigation=lambda city:{},
                    price_navigation_data=lambda data:data,publish=lambda *args:None,
                    price_route=lambda *args:{'points':[],'anchors':[],'blockers':[],'deferred':[]},
                    revisit_missed=revisit):
                cycle_route.run(1,input_file,output)
            result=json.loads(output.read_text())
            self.assertEqual(result['reason'],'completed')
            self.assertEqual(result['radarReviewed'],['new'])
            self.assertEqual(result['shops'],[])
            self.assertEqual(result['shopStats']['requests'],0)
            self.assertEqual(len(instances),1)


if __name__=='__main__': unittest.main()
