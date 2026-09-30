import copy
import unittest
from unittest.mock import patch
from route_preparation import prepare, accept_prepared, prepared_navigation
from walk_geometry import Navigation

DATA={'origin':[0,0],'extent':[-2000,2000,-2000,2000], 'obstacles':[], 'unknown':[]}
CONFIG={'city':'Giran','planningStart':[-900,0], 'targets':[
    {'name':'Trader','traderKey':'TRADER','x':0,'y':0,'object_id':123,
     'kiosk_type':1,'verification_revision':'one'}]}


class RoutePreparationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.cached=prepare(CONFIG,DATA)

    def config(self):
        return {**copy.deepcopy(CONFIG),'preparedPlan':copy.deepcopy(self.cached)}

    def test_valid_background_plan_rejoins_actual_origin_without_rebuilding_geometry(self):
        with patch('route_preparation.Navigation.__init__',side_effect=AssertionError('geometry rebuilt')):
            result=accept_prepared(self.config(),DATA,[-850,20],24)
        self.assertIsNotNone(result)
        route,planning,execution=result
        self.assertEqual(route['points'][0],(-850,20))
        self.assertTrue(all(execution.clear(a,b) for a,b in zip(route['points'],route['points'][1:])))

    def test_current_section_offline_plan_reuses_valid_map_after_late_queue_change(self):
        config=self.config();config['targets'][0]['verification_revision']='new'
        config['planningStart']=[-850,20]
        with patch('route_preparation.Navigation.__init__',side_effect=AssertionError('offline city rebuilt')):
            result=prepare(config,DATA)
        self.assertNotEqual(result['targetHash'],self.cached['targetHash'])
        accepted=accept_prepared({**config,'preparedPlan':result},DATA,[-850,20],24)
        self.assertIsNotNone(accepted)

    def test_moved_reopened_or_newly_admitted_target_discards_speculation(self):
        for field,value in [('x',10),('verification_revision','two'),('kiosk_type',3)]:
            config=self.config();config['targets'][0][field]=value
            self.assertIsNone(accept_prepared(config,DATA,[-850,0],24))
            self.assertIsNotNone(prepared_navigation(config,DATA,24))
        config=self.config();config['targets'][0]['object_id']=124
        self.assertIsNotNone(accept_prepared(config,DATA,[-850,0],24))
        config=self.config();config['targets'].append({**config['targets'][0],'name':'New'})
        self.assertIsNone(accept_prepared(config,DATA,[-850,0],24))

    def test_map_capsule_far_origin_and_blocked_connector_discard_speculation(self):
        self.assertIsNone(accept_prepared(self.config(),{**DATA,'extent':[-2100,2000,-2000,2000]},[-850,0],24))
        self.assertIsNone(accept_prepared(self.config(),DATA,[-850,0],25))
        self.assertIsNone(accept_prepared(self.config(),DATA,[3900,0],24))
        self.assertIsNone(accept_prepared(self.config(),DATA,[0,0],24))

    def test_actual_revisit_endpoint_does_not_return_to_speculative_origin(self):
        result=accept_prepared(self.config(),DATA,[900,0],24)
        self.assertIsNotNone(result)
        route,_,_=result
        self.assertEqual(route['points'][0],(900,0))
        self.assertTrue(all(p[0]>-200 for p in route['points'][1:]))

    def test_corrupt_or_incomplete_background_output_falls_back(self):
        for value in [None, 'invalid', {'schema':1}]:
            self.assertIsNone(prepared_navigation({**CONFIG,'preparedPlan':value},DATA,24))
        config=self.config();config['preparedPlan']['planning']['region']='00'
        self.assertIsNone(prepared_navigation(config,DATA,24))
        config=self.config();del config['preparedPlan']['planning']['nodes']
        self.assertIsNone(prepared_navigation(config,DATA,24))


if __name__=='__main__':unittest.main()
