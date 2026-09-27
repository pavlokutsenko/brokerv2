import sys
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]/'tools'/'BrokerWorker'/'src'
sys.path[:0]=[str(ROOT/'diagnostics'),str(ROOT/'client')]
from bind_broker_actors import merge_bindings,binding_rejection,resolve_bindings

class BindingTests(unittest.TestCase):
    def setUp(self):
        self.before={'pid':7,'module_base':100,'at':'2026-09-25T15:00:00+00:00',
            'bindings':[{'object_id':1,'name':'Before'},{'object_id':2,'name':'Known'},{'object_id':3,'name':'Radar only'}]}
        self.after={'pid':7,'module_base':100,'bindings':[{'object_id':2,'name':'Known','x':8}]}
    def test_departure_during_pass_keeps_proven_name(self):
        result=merge_bindings(self.before,self.after,'2026-09-25T15:00:01+00:00',{1,2})
        self.assertEqual({r['object_id'] for r in result},{1,2})
        self.assertEqual(result[1]['x'],8)
    def test_other_pid_rejected(self):
        self.after['pid']=8
        with self.assertRaises(RuntimeError):merge_bindings(self.before,self.after,'2026-09-25T15:00:01+00:00',{1,2})
    def test_old_snapshot_rejected(self):
        with self.assertRaises(RuntimeError):merge_bindings(self.before,self.after,'2026-09-25T15:01:00+00:00',{1,2})
    def test_replacement_module_rejected(self):
        self.after['module_base']=101
        with self.assertRaises(RuntimeError):merge_bindings(self.before,self.after,'2026-09-25T15:00:01+00:00',{1,2})
    def test_closed_shop_keeps_identity_but_not_active_type(self):
        self.assertIsNone(binding_rejection(1338059371,100,'Trader33',0,82000,148000))
        self.after['bindings']=[{'object_id':4,'name':'Trader33','kiosk_type':0}]
        result=merge_bindings(self.before,self.after,'2026-09-25T15:00:01+00:00',{4})
        self.assertEqual(result,[{'object_id':4,'name':'Trader33','kiosk_type':0}])
        self.assertEqual(merge_bindings(self.before,self.after,'2026-09-25T15:00:01+00:00',{1}),[self.before['bindings'][0]])
    def test_invalid_identity_rejected(self):
        for oid,root,name,kind,x in [(0,100,'Name',0,1),(1,0,'Name',0,1),(1,100,'',0,1),
                                    (1,100,'Name',255,1),(1,100,'Name',1,float('nan'))]:
            self.assertIsNotNone(binding_rejection(oid,root,name,kind,x,1))
    def test_intermediate_binding_survives_departure(self):
        sample={'pid':7,'module_base':100,'at':'2026-09-25T15:00:02+00:00',
            'bindings':[{'object_id':4,'name':'Transient','kiosk_type':1},{'object_id':5,'name':'Radar only'}]}
        self.after['at']='2026-09-25T15:00:03+00:00'
        result=merge_bindings(self.before,self.after,'2026-09-25T15:00:01+00:00',{4},[sample])
        self.assertEqual(result,[sample['bindings'][0]])
        for field,value in [('pid',8),('module_base',101),('at','2026-09-25T14:59:00+00:00')]:
            bad={**sample,field:value}
            with self.assertRaises(RuntimeError):
                merge_bindings(self.before,self.after,'2026-09-25T15:00:01+00:00',{4},[bad])
    def test_missing_actor_does_not_trigger_extra_broker_wait(self):
        self.after['at']='2026-09-25T15:00:03+00:00'
        seen=[]
        def take(pid,wanted):
            seen.append((pid,wanted))
            return {'pid':7,'module_base':100,'at':'2026-09-25T15:00:04+00:00',
                'bindings':[{'object_id':4,'name':'Late','kiosk_type':1}]}
        result,attempts=resolve_bindings(self.before,self.after,'2026-09-25T15:00:01+00:00',{1,4},take=take,wait=lambda _:None)
        self.assertEqual({r['object_id'] for r in result},{1})
        self.assertEqual(seen,[]);self.assertEqual(attempts,[])
    def test_missing_actor_is_left_unresolved_until_natural_next_cycle(self):
        self.after['at']='2026-09-25T15:00:03+00:00'
        waits=[]
        def take(pid,wanted):
            return {**self.after,'bindings':[]}
        result,attempts=resolve_bindings(self.before,self.after,'2026-09-25T15:00:01+00:00',{4},take=take,wait=waits.append)
        self.assertEqual(result,[]);self.assertEqual(waits,[]);self.assertEqual(attempts,[])
