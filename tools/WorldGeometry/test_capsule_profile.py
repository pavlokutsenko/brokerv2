import unittest
from types import SimpleNamespace
from unittest.mock import patch
from capsule_profile import supported_capsule
from walk_guard import WalkGuard
from walk_client import WalkClient,GWORLD
import struct


class CapsuleProfileTests(unittest.TestCase):
    def test_confirmed_shapes_only(self):
        self.assertTrue(supported_capsule(9,18));self.assertTrue(supported_capsule(9,23))
        self.assertTrue(supported_capsule(5,19))
        for size in ((12,23),(9,26),(9,22),(5,23),(float('nan'),23)):
            self.assertFalse(supported_capsule(*size))

    def test_dwarf_spawn_readiness(self):
        client=object.__new__(WalkClient);client.base=100;client.rvas={'gworld':GWORLD}
        client.world={'world':1,'controller':200,'player_actor':300,'player_capsule':400}
        client.cancelled=lambda:False
        class Memory:
            def __init__(self): self.pages={}
            def u64(self,address): return 1 if address==100+GWORLD else 300
            def unpack(self,fmt,address,_): return 9 if address==400+0x544 else 18
            def read(self,*_): return struct.pack('<3d',1,1,1)
        client.m=Memory()
        with patch('walk_client.time.sleep'): client.wait_navigation_capsule(1)
        self.assertEqual((client.capsule_radius,client.capsule_half_height),(9,18))

    def test_dwarf_floor_and_conservative_level_sweep(self):
        calls=[]
        probe=SimpleNamespace(trace=lambda a,b:calls.append((a,b)) or {'blocked':False})
        with patch('walk_guard.PawnCapsuleProbe',return_value=probe):
            guard=WalkGuard(SimpleNamespace(capsule_radius=9,capsule_half_height=18),{})
        self.assertTrue(guard.clear((0,0,18),(80,0)))
        self.assertTrue(all(a[2]==26 and b[2]==26 for a,b in calls))
        calls.clear()
        self.assertTrue(guard.clear_level_escape((0,0,18),(80,0)))
        self.assertTrue(all(a[2]==23 and b[2]==23 for a,b in calls))

    def test_female_dwarf_floor_and_conservative_level_sweep(self):
        calls=[]
        probe=SimpleNamespace(trace=lambda a,b:calls.append((a,b)) or {'blocked':False})
        with patch('walk_guard.PawnCapsuleProbe',return_value=probe):
            guard=WalkGuard(SimpleNamespace(capsule_radius=5,capsule_half_height=19),{})
        self.assertTrue(guard.clear((0,0,19),(80,0)))
        self.assertTrue(all(a[2]==26 and b[2]==26 for a,b in calls))
        calls.clear()
        self.assertTrue(guard.clear_level_escape((0,0,19),(80,0)))
        self.assertTrue(all(a[2]==23 and b[2]==23 for a,b in calls))


if __name__=='__main__': unittest.main()
