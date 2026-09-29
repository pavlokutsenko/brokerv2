import json
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch
from moving_preparation import prepare_while_moving
from route_preparation import prepare,prepared_navigation
from walk_shop_policy import trader_key

DATA={'origin':[0,0],'extent':[-1000,1200,-1000,1000],'obstacles':[],'unknown':[]}
TARGETS=[{'name':'First','traderKey':'FIRST','x':0,'y':0,'object_id':1,'kiosk_type':1},
         {'name':'Second','traderKey':'SECOND','x':400,'y':0,'object_id':2,'kiosk_type':1}]


class MovingPreparationTests(unittest.TestCase):
    def test_far_target_transit_prepares_in_background_and_restores_handoff(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);config={'city':'Giran','targets':TARGETS,'continuousSession':True,
                'preparationInput':str(root/'input.json'),'preparationOutput':str(root/'plan.json')}
            cached=prepare({**config,'planningStart':[-900,0]},DATA)
            nav=prepared_navigation({**config,'preparedPlan':cached},DATA,24)
            position=[-900,0];shops=SimpleNamespace(captured_keys=set(),dynamic_detours_enabled=True)
            client=SimpleNamespace(position=lambda:(*position,0),shops=shops,execution_clearance=24,cancelled=lambda:False)
            def follow(owner,execution,points,*args,**kwargs):
                self.assertFalse(owner.preparation_ready())
                self.assertTrue(all(execution.clear(a,b) for a,b in zip(points,points[1:])))
                position[:]=[-750,0]
                (root/'plan.json').write_text(json.dumps(cached))
                self.assertTrue(owner.preparation_ready())
                return {'reason':'background_plan_ready'}
            with patch('moving_preparation.follow_with_recovery',side_effect=follow),patch('moving_preparation.time.sleep') as sleep:
                result=prepare_while_moving(client,config,DATA,DATA,[{**t,'key':trader_key(t['name'])} for t in TARGETS],nav,None,lambda _:None,lambda _:None)
            self.assertIsNotNone(result);self.assertEqual(result[0]['points'][0],tuple(position))
            self.assertIsNone(client.preparation_ready);sleep.assert_not_called()

    def test_late_plan_computes_during_guarded_movement_and_captured_prefix_is_skipped(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);config={'city':'Giran','targets':TARGETS,'continuousSession':True,
                'preparationInput':str(root/'input.json'),'preparationOutput':str(root/'plan.json')}
            cached=prepare({**config,'planningStart':[-300,0]},DATA)
            nav=prepared_navigation({**config,'preparedPlan':cached},DATA,24)
            position=[-300,0];shops=SimpleNamespace(captured_keys=set(),dynamic_detours_enabled=True)
            client=SimpleNamespace(shops=shops,position=lambda:(*position,0),execution_clearance=24,cancelled=lambda:False)
            targets=[{**t,'key':trader_key(t['name'])} for t in TARGETS];events=[]
            def follow(owner,execution,points,*args,**kwargs):
                request=json.loads((root/'input.json').read_text())
                self.assertEqual(request['targets'],TARGETS)
                self.assertFalse(shops.dynamic_detours_enabled)
                self.assertEqual(owner.pass_goal_key,'first')
                self.assertTrue(all(execution.clear(a,b) for a,b in zip(points,points[1:])))
                position[:]=points[-1];shops.captured_keys.add('first')
                (root/'plan.json').write_text(json.dumps(cached))
                return {'reason':'completed'}
            with patch('moving_preparation.follow_with_recovery',side_effect=follow),patch('moving_preparation.time.sleep') as sleep:
                result=prepare_while_moving(client,config,DATA,DATA,targets,nav,None,events.append,lambda _:None)
            self.assertIsNotNone(result)
            self.assertEqual(result[0]['points'][0],tuple(position))
            evidence=next(e for e in events if e['type']=='moving_preparation_result')
            self.assertEqual(evidence['capturedKeys'],['first']);self.assertTrue(evidence['accepted'])
            sleep.assert_not_called()
            self.assertTrue(shops.dynamic_detours_enabled);self.assertIsNone(client.pass_goal_key)
            # First arc was already read on the prefix, so only the next body remains.
            first_approach=cached['route']['approaches'][0]['points']
            self.assertFalse(any(tuple(p) in [tuple(q) for q in result[0]['points'][1:]] for p in first_approach))

    def test_unsafe_or_too_close_prefix_never_moves_or_requests_background_work(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);config={'city':'Giran','targets':TARGETS,'continuousSession':True,
                'preparationInput':str(root/'input.json'),'preparationOutput':str(root/'plan.json')}
            client=SimpleNamespace(position=lambda:(0,0,0),shops=SimpleNamespace(captured_keys=set()))
            targets=[{**TARGETS[0],'key':'first'}]
            with patch('moving_preparation.follow_with_recovery',side_effect=AssertionError('unvalidated move')):
                result=prepare_while_moving(client,config,DATA,DATA,targets,(None,None),None,lambda _:None,lambda _:None)
            self.assertIsNone(result);self.assertFalse((root/'input.json').exists())

    def test_cancelled_prefix_returns_before_any_cache_wait_or_main_movement(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);config={'city':'Giran','targets':TARGETS,'continuousSession':True,
                'preparationInput':str(root/'input.json'),'preparationOutput':str(root/'plan.json')}
            cached=prepare({**config,'planningStart':[-300,0]},DATA)
            nav=prepared_navigation({**config,'preparedPlan':cached},DATA,24)
            shops=SimpleNamespace(captured_keys=set(),dynamic_detours_enabled=True)
            client=SimpleNamespace(position=lambda:(-300,0,0),shops=shops,cancelled=lambda:False)
            targets=[{**t,'key':trader_key(t['name'])} for t in TARGETS]
            with patch('moving_preparation.follow_with_recovery',return_value={'reason':'cancelled'}):
                result=prepare_while_moving(client,config,DATA,DATA,targets,nav,None,lambda _:None,lambda _:None)
            self.assertEqual(result,{'reason':'cancelled'})
            self.assertTrue(shops.dynamic_detours_enabled);self.assertIsNone(client.pass_goal_key)

    def test_more_productive_approaches_continue_when_first_finishes_before_plan(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);config={'city':'Giran','targets':TARGETS,'continuousSession':True,
                'preparationInput':str(root/'input.json'),'preparationOutput':str(root/'plan.json')}
            cached=prepare({**config,'planningStart':[-300,0]},DATA)
            nav=prepared_navigation({**config,'preparedPlan':cached},DATA,24)
            position=[-300,0];shops=SimpleNamespace(captured_keys=set(),dynamic_detours_enabled=True)
            client=SimpleNamespace(position=lambda:(*position,0),shops=shops,execution_clearance=24,cancelled=lambda:False)
            targets=[{**t,'key':trader_key(t['name'])} for t in TARGETS];hops=[];events=[]
            def follow(owner,execution,points,*args,**kwargs):
                hops.append(owner.pass_goal_key);position[:]=points[-1]
                shops.captured_keys.add(owner.pass_goal_key)
                if len(hops)==2:(root/'plan.json').write_text(json.dumps(cached))
                return {'reason':'completed'}
            with patch('moving_preparation.follow_with_recovery',side_effect=follow),patch('moving_preparation.time.sleep') as sleep:
                result=prepare_while_moving(client,config,DATA,DATA,targets,nav,None,events.append,lambda _:None)
            self.assertEqual(hops,['first','second']);sleep.assert_not_called()
            self.assertIsNotNone(result)
            self.assertEqual(result[0]['points'],[])
            evidence=next(e for e in events if e['type']=='moving_preparation_result')
            self.assertEqual(evidence['hops'],2);self.assertTrue(evidence['accepted'])


if __name__=='__main__':unittest.main()
