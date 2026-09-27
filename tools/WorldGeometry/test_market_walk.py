import math
import json
import struct
import unittest
from pathlib import Path as FsPath
from unittest.mock import patch
import numpy as np
from shapely.geometry import Point

from walk_geometry import Navigation,rounded_route
from walk_plan import nearby_segments
from walk_follow import Path,follow,shop_lookahead,temple_gate_direct_allowed,gate_center_goal,lookahead_goal
from walk_recovery import learn,rejoin,follow_with_recovery,unstick
from ground_surface import cliff_edges
from walk_guard import WalkGuard
from walk_client import WalkClient
from inspect_world import GWORLD
from walk_escape import escape
from cycle_plan import price_navigation_data
from city_maps import load_navigation
from functools import partial
from types import SimpleNamespace
import threading

GIRAN_RULES=load_navigation('Giran')['navigationRules']
temple_gate_direct_allowed=partial(temple_gate_direct_allowed,rules=GIRAN_RULES)


class MarketWalkTests(unittest.TestCase):
    def test_game_accepted_endpoint_reads_instead_of_waiting_for_immobility(self):
        shops=SimpleNamespace(active=threading.Event(),error=None,captured_keys=set(),
                              ignored_outside_keys=set(),anchor_positions=[(100,0)],needs_resume=lambda:False)
        client=SimpleNamespace(shops=shops,read_goal_key='shop',initial_selected=0,world={'controller':0},
                               m=SimpleNamespace(u64=lambda _:0),position=lambda:(94.42,0,0),
                               cancelled=lambda:False,move=lambda _:None,stop=lambda:None)
        nav=SimpleNamespace(clear=lambda *_:True)
        def read(*_): shops.captured_keys.add('shop');return 'captured'
        with patch('walk_follow.arrival_read',side_effect=read),patch('walk_follow.time.sleep'):
            result=follow(client,nav,[(0,0),(100,0)],lambda _:None,max_seconds=5)
        self.assertEqual(result['reason'],'completed')
        self.assertTrue(result['handoff'])
        self.assertLess(result['seconds'],1)

    def test_folded_lookahead_near_pawn_replans_instead_of_repeating_noop_move(self):
        # V40 temple: repeated destination only3.18units from stationary pawn.
        path=Path([(0,0),(40,0),(3,0),(100,0)])
        class Nav:
            def clear(self,start,end): return math.dist(start,end)<4
        self.assertIsNone(lookahead_goal(path,Nav(),(0,0,0),0,77))

    def test_folded_lookahead_tries_another_clear_goal(self):
        path=Path([(0,0),(40,0),(3,0),(100,0)])
        class Nav:
            def clear(self,start,end): return True
        goal=lookahead_goal(path,Nav(),(0,0,0),0,77)
        self.assertGreaterEqual(math.dist((0,0),goal),8)

    def test_near_final_arrival_keeps_completion_in_outer_loop(self):
        path=Path([(0,0),(100,0)])
        class Nav:
            def clear(self,start,end): return True
        self.assertEqual(lookahead_goal(path,Nav(),(97,0,0),97,95),(100,0))

    def navigation(self):
        return Navigation({"extent":[0,700,0,600],"obstacles":[{"rings":[[[280,120],[350,120],[350,440],[280,440],[280,120]]]}],
                           "unknown":[{"rings":[[[450,100],[520,100],[520,160],[450,160],[450,100]]]}]},clearance=25,cell=25)

    def test_route_rounds_around_wall_without_cutting_unknown_area(self):
        nav=self.navigation();start=(80100,147280);end=(80600,147280)
        self.assertFalse(nav.clear(start,end))
        route=rounded_route(nav.shortest(start,end),nav)
        self.assertEqual(route[0],start);self.assertEqual(route[-1],end)
        self.assertTrue(all(nav.clear(a,b) for a,b in zip(route,route[1:])))
        self.assertGreater(sum(math.dist(a,b) for a,b in zip(route,route[1:])),math.dist(start,end))
        self.assertFalse(nav.free.covers(Point(80480,147130)))

    def test_coverage_includes_segment_middle_and_excludes_distant_points(self):
        points=np.array([[50,8],[50,25],[100,0]])
        self.assertEqual(nearby_segments(points,[(0,0),(100,0)],10).tolist(),[True,False,True])

    def test_execution_tolerates_entry_into_planning_margin(self):
        data={"extent":[0,700,0,600],"obstacles":[{"rings":[[[280,120],[350,120],[350,440],[280,440],[280,120]]]}],"unknown":[]}
        planning=Navigation(data,clearance=55,cell=25)
        execution=Navigation(data,clearance=24,cell=25)
        point=Point(80404,147280)  # 54 units from the wall, capsule radius is 9.
        self.assertFalse(planning.free.covers(point))
        self.assertTrue(execution.free.covers(point))
        self.assertFalse(execution.free.covers(Point(80360,147280)))

    def test_projection_does_not_jump_to_later_route_crossing(self):
        p=Path([(0,0),(100,0),(100,400),(0,400),(0,0),(100,0)])
        arc,error=p.project((50,0),40)
        self.assertAlmostEqual(arc,50);self.assertEqual(error,0)

    def test_price_anchor_prevents_a_lookahead_skip(self):
        class Shops:
            anchor_positions=[(150,0)]
        self.assertEqual(shop_lookahead((0,0,0),Shops(),210),95)
        self.assertEqual(shop_lookahead((500,0,0),Shops(),210),210)

    def test_diagonal_gate_approach_shortens_goal_before_entering_gate_y_band(self):
        # Observed V36: long diagonal goal reached the arch but exceeded the
        # bounded160 probe while the pawn was32 units below its Y band.
        self.assertEqual(shop_lookahead((83617,148503,-3409),None,175,GIRAN_RULES),95)

    def test_gate_crossing_stays_straight_past_side_pillar(self):
        self.assertEqual(gate_center_goal((83824,149058,-3398),(83913,149033),GIRAN_RULES),
                         (83913,149060))
        self.assertEqual(gate_center_goal((83990,149058,-3402),(84080,149033),GIRAN_RULES),
                         (84080,149033))
        self.assertEqual(gate_center_goal((83710,149058,-3409),(83715,149120),GIRAN_RULES),
                         (83715,149120))

    def test_sloped_facade_tread_probe_is_confined_to_arch_centerline(self):
        hit={'outer':'Giran_CH_front_body','point':[83848.3,148620.8,-3420.6],
             'normal':[.77423,-.28588,.56466]}
        current=(83885.2,148615.2,-3397.7);goal=(83793.4,148625)
        self.assertTrue(temple_gate_direct_allowed(current,goal,hit))
        self.assertFalse(temple_gate_direct_allowed(current,(83793.4,148590),hit))
        self.assertFalse(temple_gate_direct_allowed(current,goal,{**hit,'point':[83848.3,148690,-3420.6]}))
        self.assertFalse(temple_gate_direct_allowed(current,goal,{**hit,'normal':[.77423,-.28588,0]}))
        self.assertFalse(temple_gate_direct_allowed(current,goal,{**hit,'outer':'market_post'}))

    def test_price_navigation_keeps_temple_walls_but_drops_broad_capsule_bands(self):
        wall={'name':'Giran_CH_front_body','rings':[[[100,100],[110,100],[110,110],[100,110],[100,100]]]}
        band={'name':'Giran_CH_in_front [capsule band]','rings':[[[50,50],[200,50],[200,200],[50,200],[50,50]]]}
        observed={'name':'Giran_CH_front_body [capsule band]','rings':[[[240,240],[250,240],[250,250],[240,250],[240,240]]]}
        ordinary={'name':'market_post [capsule band]','rings':[[[300,300],[310,300],[310,310],[300,310],[300,300]]]}
        raw={'obstacles':[wall,band,observed,ordinary],'unknown':[],'extent':[0,500,0,500],
             'navigationRules':GIRAN_RULES}
        filtered=price_navigation_data(raw)
        self.assertEqual(filtered['obstacles'],[wall,observed,ordinary])
        self.assertEqual(raw['obstacles'],[wall,band,observed,ordinary])
        self.assertTrue(Navigation(filtered,clearance=0).clear((80150,147150),(80150,147150)))

    def test_three_real_temple_section_gaps_survive_navigation_inflation(self):
        data=load_navigation('Giran')
        nav=Navigation(price_navigation_data(data))
        for y in (148185,148620,149060):
            self.assertTrue(nav.clear((83700,y),(83950,y)),y)
        self.assertFalse(nav.clear((83700,148800),(83950,148800)))

    def test_direct_temple_probe_is_confined_to_three_entrances(self):
        hit={'outer':'Giran_CH_front_body','point':[83772,148645,-3420],
             'normal':[-1,0,0]}
        self.assertTrue(temple_gate_direct_allowed((83732,148635,-3406),(83823,148665),hit))
        self.assertTrue(temple_gate_direct_allowed((83647,148624,-3409),(83742,148624),
            {**hit,'point':[83745,148624,-3425]}))
        self.assertFalse(temple_gate_direct_allowed((83647,148624,-3409),(83720,148624),
            {**hit,'point':[83745,148624,-3425]}))
        self.assertTrue(temple_gate_direct_allowed((83690,148646,-3409),(83775,148599),
            {**hit,'point':[83745,148619,-3425]}))
        self.assertTrue(temple_gate_direct_allowed((83823,148665,-3406),(83732,148635),hit))
        self.assertTrue(temple_gate_direct_allowed((83777,148178,-3398),(83934,148162),
            {**hit,'outer':'Giran_CH_side_body','point':[83817,148174,-3420]}))
        self.assertFalse(temple_gate_direct_allowed((83732,148800,-3406),(83823,148800),
            {**hit,'point':[83772,148800,-3420]}))
        self.assertFalse(temple_gate_direct_allowed((83732,148635,-3406),(83823,148665),
            {**hit,'outer':'market_post'}))

    def test_cancel_issues_stop_without_any_movement(self):
        class Client:
            def __init__(self): self.stopped=False
            def cancelled(self): return True
            def position(self): return (0,0,0)
            def stop(self): self.stopped=True
            def move(self,_): raise AssertionError("cancelled test must not move")
        c=Client()
        with patch('walk_follow.time.sleep'):
            result=follow(c,None,[(0,0),(100,0)],lambda _:None)
        self.assertEqual(result['reason'],'cancelled');self.assertTrue(c.stopped)

    def test_learned_contact_invalidates_route_without_trapping_current(self):
        nav=self.navigation();current=(80100,147080);goal=(80350,147080)
        self.assertTrue(nav.clear(current,goal))
        result=learn(nav,current,goal,{'point':(80210,147080,0)})
        self.assertEqual(result['radius'],42)
        self.assertTrue(nav.clear(current,current))
        self.assertFalse(nav.clear(current,goal))
        route=nav.shortest(current,goal)
        self.assertTrue(all(nav.clear(a,b) for a,b in zip(route,route[1:])))

    def test_cliff_blocks_large_height_change_but_preserves_steps(self):
        grid={'origin':[0,0],'step':20,'nx':3,'ny':2,'values':[0,8,70,0,8,70]}
        walls=cliff_edges(grid)
        self.assertFalse(walls.covers(Point(10,5)))
        self.assertTrue(walls.covers(Point(30,5)))

    def test_native_hit_stops_before_movement_command(self):
        class Client:
            initial_selected=0;world={'controller':0}
            class m:
                @staticmethod
                def u64(_): return 0
            def cancelled(self): return False
            def position(self): return (0,0,0)
            def stop(self): pass
            def move(self,_): raise AssertionError('blocked segment must not be commanded')
        class Guard:
            last_hit={'blocked':True}
            def clear(self,*_): return False
        class Nav:
            def clear(self,*_): return True
        with patch('walk_follow.time.sleep'):
            result=follow(Client(),Nav(),[(0,0),(100,0)],lambda _:None,guard=Guard())
        self.assertEqual(result['reason'],'blocked');self.assertEqual(result['commands'],0)

    def test_escape_can_leave_a_falsely_low_floor_band_at_actual_height(self):
        class Client:
            point=(0,0,5)
            moves=[]
            def position(self): return self.point
            def cancelled(self): return False
            def move(self,goal): self.moves.append(goal);self.point=(*goal,5)
            def stop(self): pass
        class Nav:
            nodes={(1,1):(80,0)}
            def clear(self,a,b): return a==(80,0)
            def clear_forbidden(self,a,b): return True
        class Guard:
            last_hit={'blocked':True,'normal':(0,0,1)}
            def clear(self,*_): return False
            def clear_level_escape(self,*_): return True
        client=Client();events=[]
        with patch('walk_escape.time.sleep'):
            self.assertTrue(escape(client,Nav(),Guard(),events.append,float('inf')))
        self.assertEqual(client.moves,[(80,0)])
        self.assertIn('escape_level_clear',[event['type'] for event in events])

    def test_recovery_stops_after_bounded_repeated_failures(self):
        class Client:
            def cancelled(self): return False
            def position(self): return (80100,147080,0)
        class Guard:
            last_hit={'point':(80210,147080,0)};queries=0
        outcome={'reason':'blocked','progress':0,'commands':0,'traveled':0,'peak_route_error':0}
        points=[(80100,147080),(80350,147080)]
        with patch('walk_recovery.follow',side_effect=lambda *a,**kw:dict(outcome)) as mover:
            with patch('walk_recovery.rejoin',return_value=(points,160)):
                result=follow_with_recovery(Client(),self.navigation(),points,lambda _:None,60,None,Guard())
        self.assertEqual(result['reason'],'recovery_limit')
        self.assertEqual(mover.call_count,5)
        self.assertTrue(result['recoveries'][-1]['deferred'])

    def test_unstick_requires_observed_departure(self):
        class Client:
            point=(100,100,0)
            def cancelled(self): return False
            def position(self): return self.point
            def move(self,goal): self.point=(*goal,0)
            def stop(self): pass
        class Nav:
            def clear(self,*_): return True
        class Guard:
            def clear(self,*_): return True
        events=[]
        self.assertTrue(unstick(Client(),Nav(),Guard(),events.append,float('inf')))
        self.assertEqual(events[0]['type'],'unstick_success')
        self.assertEqual(events[0]['position'][:2],(35,100))

    def test_radar_detour_reads_before_escaping_another_anchor_disk(self):
        start=(80100,147080);end=(80210,147180)
        nav=self.navigation();nav.add_blocker(Point(end).buffer(45))
        class Shops:
            detour_target={'key':'new','x':80200,'y':147200}
            anchor_positions=[];requested_keys=set();dynamic_detours_enabled=True
        class Client:
            point=(*start,0);shops=Shops();execution_clearance=24
            def position(self): return self.point
            def cancelled(self): return False
        class Guard:
            queries=0
        client=Client();events=[];calls=[]
        def move(c,n,points,*args):
            calls.append(points)
            reason='radar_detour' if len(calls)==1 else 'completed'
            if len(calls)>1: client.point=(*points[-1],0)
            return {'reason':reason,'progress':0,'commands':0,'traveled':0,
                    'peak_route_error':0,'position':client.point}
        data={'extent':[0,700,0,600],'obstacles':[],'unknown':[]}
        def leave(*_):
            client.point=(*start,0)
            return True
        with patch('walk_recovery.follow',side_effect=move), \
                patch('walk_recovery.radar_pass_route',return_value=None), \
                patch('walk_recovery.recheck_route',return_value=([start,end],self.navigation(),False)), \
                patch('walk_recovery.escape',side_effect=leave):
            result=follow_with_recovery(client,nav,[start,(80350,147080)],events.append,60,None,Guard(),data)
        self.assertEqual(result['reason'],'completed')
        self.assertEqual(calls[1],[start,end])
        self.assertIn('radar_detour_rejoin_needs_escape',[e['type'] for e in events])

    def test_step_retry_requires_a_clear_raised_sweep(self):
        class Ground:
            def height(self,*_): return 0
        class Probe:
            def __init__(self,raised_blocked): self.raised_blocked=raised_blocked;self.calls=0
            def trace(self,*_):
                self.calls+=1
                return {'blocked':self.calls==1 or self.raised_blocked,'outer':'Giran_V_Plaza_Stair01',
                        'point':(20,0,8),'normal':(-1,0,0)}
        for raised_blocked in (False,True):
            guard=object.__new__(WalkGuard);guard.ground=Ground();guard.probe=Probe(raised_blocked);guard.rules=GIRAN_RULES
            guard.queries=0;guard.last_hit=None
            self.assertEqual(guard.clear((0,0,23),(40,0)),not raised_blocked)
            self.assertEqual(guard.queries,2)

    def test_temple_low_tread_retries_upward_but_vertical_wall_does_not(self):
        class Ground:
            def height(self,*_): return -3432
        class Probe:
            def __init__(self,normal): self.normal=normal;self.calls=0
            def trace(self,*_):
                self.calls+=1
                return {'blocked':self.calls==1,'outer':'Giran_CH_side_body',
                        'point':(83796,148185,-3420),'normal':self.normal}
        for normal,expected in (((0,0,1),True),((-1,0,0),False)):
            guard=object.__new__(WalkGuard);guard.ground=Ground();guard.probe=Probe(normal)
            guard.queries=0;guard.last_hit=None;guard.step_overrides=0
            self.assertEqual(guard.clear((83828,148188,-3397),(83780,148185)),expected)
            self.assertEqual(guard.queries,3 if expected else 1)

    def test_slanted_temple_riser_requires_raised_clearance_and_local_band(self):
        class Ground:
            def height(self,*_): return -3432
        class Probe:
            def __init__(self,x,raised): self.x=x;self.raised=raised;self.calls=0
            def trace(self,*_):
                self.calls+=1
                return {'blocked':self.calls==1 or self.raised,
                        'outer':'BspConvertedToStaticMesh_021E62DC',
                        'point':(self.x,148624,-3424),'normal':(.65,0,.758)}
        for x,raised,expected in ((83871,False,True),(83871,True,False),(84000,False,False)):
            guard=object.__new__(WalkGuard);guard.ground=Ground();guard.probe=Probe(x,raised);guard.rules=GIRAN_RULES
            guard.queries=0;guard.last_hit=None;guard.step_overrides=0
            self.assertEqual(guard.clear((83814,148624,-3397),(83850,148624)),expected)

    def test_temple_gate_limits_lookahead_without_a_shop_reader(self):
        for y in (148185,148625,149060):
            self.assertEqual(shop_lookahead((83730,y,0),None,210,GIRAN_RULES),95)
        self.assertEqual(shop_lookahead((83730,148800,0),None,210),210)

    def test_live_world_guard_rejects_changed_world_despite_old_cached_page(self):
        class Memory:
            def __init__(self): self.pages={1:b'old snapshot'}
            def u64(self,_): return 100 if self.pages else 200
        client=object.__new__(WalkClient)
        client.m=Memory();client.base=0;client.world={'world':100}
        with self.assertRaisesRegex(RuntimeError,'world/pawn changed'):
            client.position()

    def test_new_client_waits_for_validated_spawn_capsule(self):
        client=object.__new__(WalkClient)
        client.base=100
        client.world={'world':1,'controller':200,'player_actor':300,'player_capsule':400}
        client.cancelled=lambda:False
        class Memory:
            def __init__(self): self.pages={};self.poll=0
            def u64(self,address):
                if address==100+GWORLD:
                    self.poll+=1
                    return 1
                return 300
            def unpack(self,fmt,address,offset):
                if address==400+0x544: return 12 if self.poll==1 else 9
                return 23
            def read(self,address,size): return struct.pack('<3d',1,1,1)
        client.m=Memory()
        with patch('walk_client.time.sleep'):
            client.wait_navigation_capsule(1)
        self.assertEqual(client.m.poll,3)
        self.assertEqual((client.capsule_radius,client.capsule_half_height),(9,23))


if __name__=='__main__':
    unittest.main()
