"""An exterior radar read must not reconnect to old interior coverage."""
import unittest
from types import SimpleNamespace
from unittest.mock import Mock,patch
from walk_recovery import follow_with_recovery


class DetourContinuationTests(unittest.TestCase):
    def test_exterior_read_keeps_actual_origin_for_all_remaining_assignments(self):
        position=[0,0,0];target={'key':'new','x':1000,'y':0}
        shops=SimpleNamespace(detour_target=target,anchor_positions=[(0,0)],priority_keys={'old'},
            dynamic_detours_enabled=True,requested_keys={'new'},captured_keys=set())
        nav=SimpleNamespace(clear=lambda *_:True)
        client=SimpleNamespace(shops=shops,execution_clearance=24,position=lambda:tuple(position),
            route_navigation=(nav,nav))
        def moving(*args,**kwargs):
            exterior=len(calls)>0;calls.append(args[2])
            if exterior:position[:]=[1000,0,0];shops.captured_keys.add('new')
            return {'reason':'completed' if exterior else 'radar_detour','progress':10,
                'position':tuple(position),'commands':1,'traveled':1000 if exterior else 10,'peak_route_error':0}
        calls=[];events=[];guard=SimpleNamespace(queries=2)
        with patch('walk_recovery.follow',side_effect=moving),\
             patch('walk_recovery.execution_navigation',return_value=nav),\
             patch('walk_recovery.radar_pass_route',return_value=([(0,0),(1000,0)],nav)),\
             patch('walk_recovery.rejoin',side_effect=AssertionError('back into read room')):
            result=follow_with_recovery(client,nav,[(0,0),(100,0)],events.append,60,None,guard,{})
        self.assertEqual(result['reason'],'replan_required')
        self.assertEqual(result['position'],(1000,0,0));self.assertEqual(len(calls),2)
        self.assertIn('new',shops.captured_keys)
        self.assertTrue(shops.dynamic_detours_enabled);self.assertEqual(shops.priority_keys,{'old'})
        self.assertTrue(any(e.get('avoided_old_rejoin') for e in events))


if __name__=='__main__':unittest.main()
